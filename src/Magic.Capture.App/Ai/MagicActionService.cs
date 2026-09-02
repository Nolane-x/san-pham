using Magic.Capture.App.Ai.Provider;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Commerce;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Utilities;
using System.Text;

namespace Magic.Capture.App.Ai;

internal sealed class MagicActionService
{
    private readonly ScreenGraphService _graphs;
    private readonly AiProviderProfileStore _profiles;
    private readonly AiProviderClientFactory _clients;
    private readonly EntitlementService _entitlements;
    private readonly Func<Magic.Capture.Core.Settings.AppSettings> _settings;
    private readonly AiImagePreprocessor _images;
    private readonly AiResultCache _cache;

    public MagicActionService(ScreenGraphService graphs, AiProviderProfileStore profiles, AiProviderClientFactory clients,
        EntitlementService entitlements, Func<Magic.Capture.Core.Settings.AppSettings> settings, AiImagePreprocessor images, AiResultCache cache)
    {
        _graphs = graphs;
        _profiles = profiles;
        _clients = clients;
        _entitlements = entitlements;
        _settings = settings;
        _images = images;
        _cache = cache;
    }

    public async Task<MagicActionExecutionResult> ExecuteAsync(MagicActionExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (!_entitlements.CanUse(ProductFeature.MagicActions)) throw new InvalidOperationException("Magic AI is available in Pro Lifetime.");
        var (state, profile) = await ResolveProfileAsync(request.Action, cancellationToken);
        var graph = await _graphs.BuildAsync(request.Primary, _settings(), cancellationToken);
        var contextGraphs = new List<(CaptureAsset Asset, Magic.Capture.Core.ScreenGraph.ScreenGraphDocument Graph)>();
        foreach (var asset in request.Context.Take(7))
            contextGraphs.Add((asset, await _graphs.BuildAsync(asset, _settings(), cancellationToken)));

        var model = profile.ToModelProfile();
        if ((model.Capabilities & request.Action.MinimumCapabilities) != request.Action.MinimumCapabilities)
            throw new InvalidOperationException($"The selected model does not provide required capability: {request.Action.MinimumCapabilities}.");
        if (request.Action.VisionMode == MagicActionVisionMode.Required && !model.Has(AiCapability.VisionInput))
            throw new InvalidOperationException("This Magic Action requires a vision-capable model. Choose a vision model or another action.");

        var plan = AiContextPlanner.Plan(request.Action, model, request.Primary.Id, contextGraphs.Select(x => x.Asset.Id).ToArray(), AiPrivacyPolicy.ToCore(state.Privacy, profile));
        if (request.Action.VisionMode == MagicActionVisionMode.Required && !plan.IncludePrimaryImage)
            throw new InvalidOperationException("This action requires image context, but the current privacy/model configuration blocks image input.");

        var prompt = MagicPromptCompiler.Compile(request.Action, graph, request.UserQuestion, contextGraphs.Count > 0 ? "p" : null);
        if (contextGraphs.Count > 0)
        {
            prompt += "\n\nCONTEXT STACK (supporting captures; evidence IDs are local to each context block):\n" + string.Join("\n\n", contextGraphs.Select((x, i) =>
                $"--- Context {i + 1}: {x.Asset.SourceDisplayName ?? x.Asset.SourceKind.ToString()} ---\n" + MagicPromptCompiler.SerializeGraph(x.Graph, model.ContextSize == AiContextSizeClass.Small ? 120 : 300, $"c{i + 1}")));
        }

        var images = new List<AiImageAttachment>();
        if (plan.IncludePrimaryImage) images.Add(new AiImageAttachment("image/png", _images.Prepare(request.Primary, model), "Primary capture"));
        foreach (var id in plan.ContextImageIds)
        {
            var match = contextGraphs.FirstOrDefault(x => x.Asset.Id == id);
            if (match.Asset is not null)
                images.Add(new AiImageAttachment("image/png", _images.Prepare(match.Asset, model), match.Asset.SourceDisplayName ?? "Context capture"));
        }

        var settings = _settings();
        var contextHashes = contextGraphs.Select(x => HashUtility.ComputeSha256(x.Asset.PngBytes)).ToList();
        if (!string.IsNullOrWhiteSpace(request.UserQuestion))
            contextHashes.Add(HashUtility.ComputeSha256(Encoding.UTF8.GetBytes(request.UserQuestion)));
        var promptHash = HashUtility.ComputeSha256(Encoding.UTF8.GetBytes(prompt));
        var imagePayloadHash = images.Count == 0
            ? "none"
            : HashUtility.ComputeSha256(Encoding.UTF8.GetBytes(string.Join("|", images.Select(image => HashUtility.ComputeSha256(image.Bytes)))));
        var strategy = $"images={plan.Summary.ImageCount};graph={plan.Summary.UsesScreenGraph};context={plan.Summary.ContextItemCount};prompt={promptHash};image={imagePayloadHash}";
        var cacheKey = AiCacheKey.Create(
            HashUtility.ComputeSha256(request.Primary.PngBytes),
            contextHashes,
            request.Action.Id,
            request.Action.SchemaVersion,
            profile.Id.ToString("N"),
            profile.ModelId,
            strategy);

        AiActionResult result;
        var fromCache = false;
        if (settings.EnableAiResultCache && _entitlements.CanUse(ProductFeature.AiResultCache))
        {
            var cached = await _cache.TryGetAsync(cacheKey, TimeSpan.FromDays(Math.Clamp(settings.AiCacheMaximumAgeDays, 1, 365)), cancellationToken);
            if (cached is not null)
            {
                result = cached.Result;
                fromCache = true;
            }
            else
            {
                var client = _clients.Create(profile);
                var response = await client.GenerateAsync(new AiProviderRequest(prompt, request.Action.OutputKind, images), cancellationToken);
                result = AiResponseParser.Parse(response.Text, request.Action.Name);
                await _cache.TryPutAsync(new AiResultCacheEntry(cacheKey, DateTimeOffset.UtcNow, request.Action.Id, profile.Id.ToString("N"), profile.ModelId, result), settings.AiCacheMaximumEntries, cancellationToken);
            }
        }
        else
        {
            var client = _clients.Create(profile);
            var response = await client.GenerateAsync(new AiProviderRequest(prompt, request.Action.OutputKind, images), cancellationToken);
            result = AiResponseParser.Parse(response.Text, request.Action.Name);
        }

        var evidence = new List<ResolvedEvidence>();
        if (contextGraphs.Count == 0)
        {
            evidence.AddRange(EvidenceResolver.Resolve(graph, result.EvidenceIds));
        }
        else
        {
            evidence.AddRange(EvidenceResolver.Resolve(graph, result.EvidenceIds, "p"));
            for (var i = 0; i < contextGraphs.Count; i++)
                evidence.AddRange(EvidenceResolver.Resolve(contextGraphs[i].Graph, result.EvidenceIds, $"c{i + 1}"));
        }
        return new MagicActionExecutionResult(result, evidence, graph, plan.Summary, profile.DisplayName, profile.ModelId, profile.IsLocal, fromCache);
    }
    public async Task<MagicActionExecutionPreview> PreviewAsync(MagicActionExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (!_entitlements.CanUse(ProductFeature.MagicActions)) throw new InvalidOperationException("Magic AI is available in Pro Lifetime.");
        var (state, profile) = await ResolveProfileAsync(request.Action, cancellationToken);
        var model = profile.ToModelProfile();
        var plan = AiContextPlanner.Plan(request.Action, model, request.Primary.Id, request.Context.Take(7).Select(x => x.Id).ToArray(),
            AiPrivacyPolicy.ToCore(state.Privacy, profile));
        if (request.Action.VisionMode == MagicActionVisionMode.Required && !plan.IncludePrimaryImage)
            throw new InvalidOperationException("This action requires image context, but the current privacy/model configuration blocks image input.");
        return new MagicActionExecutionPreview(profile.DisplayName, profile.ModelId, profile.IsLocal, plan.Summary, state.Privacy.RoutingMode);
    }

    private async Task<(AiProviderProfileState State, AiProviderProfile Profile)> ResolveProfileAsync(
        MagicActionDefinition action, CancellationToken cancellationToken)
    {
        var state = await _profiles.LoadAsync(cancellationToken);
        var enabled = state.Profiles.Where(p => p.Enabled && (!state.Privacy.LocalProvidersOnly || p.IsLocal)).ToArray();
        if (enabled.Length == 0)
            throw new InvalidOperationException(state.Privacy.LocalProvidersOnly
                ? "No enabled local AI provider is configured. Add Ollama, LM Studio, or another localhost endpoint in AI & Magic settings."
                : "Configure and enable an AI provider in AI & Magic settings first.");

        var ranked = AiProviderRouter.Rank(action, enabled.Select(p => new AiProviderCandidate(
            p.Id.ToString("N"), p.ToModelProfile(), p.Id == state.ActiveProfileId, p.IsLocal)), state.Privacy.RoutingMode);
        var selected = ranked.FirstOrDefault()
            ?? throw new InvalidOperationException("No configured AI model satisfies this Magic Action's capabilities. Adjust provider capabilities or choose another action.");
        var profile = enabled.First(p => p.Id.ToString("N") == selected.Id);
        AiPrivacyPolicy.Validate(state.Privacy, profile);
        return (state, profile);
    }

}
