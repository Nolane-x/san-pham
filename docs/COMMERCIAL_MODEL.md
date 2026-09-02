# Magic Capture Desktop 2.0 — Commercial Model

## Product ladder

```text
Free forever
   ↓ first successful run
Plus trial — 168 hours
   ↓ expiry
Free forever

Pro Lifetime may be purchased at any time through Microsoft Store.
```

There is only **one paid product**: Pro Lifetime.

## Free

Free is a permanent capture utility, not a crippled launch screen. It includes the everyday capture foundation, local OCR, basic editor/pins/history, deterministic signals, basic workflows, timed capture and selected local utilities.

Free does not stop working when Plus expires.

## Plus — trial only

Plus exists only to let a new installation experience advanced deterministic functionality for exactly 168 hours.

Plus:

- has no Store SKU;
- cannot be purchased;
- requests no payment method;
- never auto-renews;
- never causes a charge;
- contains **no generative AI capability**.

When Plus expires, existing captures/settings remain and the product returns to Free.

## Pro Lifetime

Pro is the only paid tier. It is a Microsoft Store Durable / Forever add-on.

Pro permanently unlocks all Plus capabilities plus the features that justify the commercial product:

- repeat-last-region / fixed-aspect power capture;
- Compare Workspace and semantic compare;
- click-through pins and Pro history options;
- custom destinations;
- full AI provider/runtime configuration;
- local AI and BYOK cloud AI;
- ScreenGraph AI context;
- Magic Actions and custom actions;
- Context Stack;
- evidence anchoring;
- AI Guard and AI result cache;
- Magic Recipes / hybrid deterministic+AI pipelines.

AI is intentionally **Pro-only**. Plus is not an AI trial.

## AI business model

Magic Capture Desktop does not operate an AI inference service and does not resell tokens.

A Pro purchase licenses the software integration/runtime. The user supplies:

- their cloud provider API key/account; or
- their own local model runtime such as Ollama/LM Studio/OpenAI-compatible local server.

Provider charges, quotas and provider-side data policies remain between the user and that provider. Local inference uses the user's own hardware.

This keeps Magic Capture Desktop's lifetime-license economics compatible with AI without creating an unbounded developer inference bill.

## US pricing

```text
Magic Capture Desktop app       Free
Plus                            Not sold
Pro Lifetime regular MSRP       US $29.99
Pro Lifetime launch sale        US $19.99
Launch sale duration            90 consecutive days
Subscription                    None
Recurring billing               None
```

The 90-day launch period starts when Pro Lifetime becomes publicly purchasable, not when source is tagged or certification begins.

## Store-driven price display

No customer-facing US price is embedded as an application constant. Magic Capture Desktop queries the Microsoft Store product price and displays the localized current Store price.

The Store remains responsible for market currency formatting, taxes/price-tier behavior and sale timing.

## Upgrade UX rules

- No forced Magic Capture account.
- No payment method before Plus starts.
- No automatic checkout at Plus expiry.
- Plus expiry never deletes captures/settings.
- Free remains useful forever.
- Pro-only features remain visible with restrained `PRO` labels when visibility helps explain product value.
- Plus features may remain visible with `PLUS` labels after trial expiry.
- AI settings/actions are Pro-only; Plus does not secretly call AI.
- A Store outage must not block ordinary local capture.
- A temporary Store failure must not casually downgrade an installation previously confirmed as Pro.

## AI privacy messaging

The purchase page should state clearly:

> Pro includes AI integration. Magic Capture Desktop does not include AI usage credits. Connect your own supported cloud API account or local model. Cloud AI requests are sent directly from your PC to the provider you configure.

Do not imply that buying Pro includes unlimited OpenAI/Anthropic/Gemini usage.

## Partner Center structure

Create one free app plus one paid Durable/Forever add-on:

```text
App:              Magic Capture Desktop          Free
Add-on:           Magic Capture Desktop Pro      Durable / Forever
Offer token:      magiccapture.desktop.pro
Plus add-on:      DO NOT CREATE
Subscription:     DO NOT CREATE
```

See `packaging/STORE_SUBMISSION.md` for the release matrix.
