# Design

How the Passless interface is built, and why it looks the way it does. The
implementation lives in [`client/src/design/`](../client/src/design/); every
specimen in this document can be seen in both themes at `/preview`.

## The one rule

> **The security-warning colour is reserved. It must never be used
> decoratively.**

`--pl-warn` (`#FF7A18`) and its derivatives are permitted in exactly four
places, and nowhere else:

1. Removing the last passkey from an account.
2. Confirming sign-out-everywhere.
3. A `critical` row in the security activity log.
4. An unrecognised-credential error during an assertion.

Permitted forms in those four places: a fill (`bg-warn` with `text-on-warn`), a
3px top edge on the confirmation dialog, the 1px `--pl-warn-edge` beneath a warn
fill, and `--pl-warn-text` for the eyebrow label. Nothing else — no hover state,
no chart series, no badge, no focus ring, no icon tint, no "attention" styling of
any kind.

The rule is worth stating this bluntly because its value is entirely negative:
the colour means "a security decision here has consequences" **only** as long as
it never appears when that is untrue. Every decorative use spends a little of
the signal, and the spending is invisible until the day a user scrolls past a
genuine warning because amber is just what this product looks like.

Two consequences that are easy to get wrong:

- **"Platform not supported" is not a warning.** A browser without WebAuthn is a
  capability fact, not a threat. It renders in neutral tokens. Treating a missing
  authenticator as an alarm teaches users to discount the colour.
- **A cancelled ceremony and a timeout are not warnings either.** Only the
  `unrecognised` error implies a credential the server rejected. The other two
  get a neutral rule in the same layout, so the amber rule stays meaningful when
  it does appear.

Enforced in review, not by the compiler. The narrower rule that *no component
names a colour at all* is checked mechanically:

```bash
npm --prefix client run check:colours
```

## Position

Passless is a reference implementation that developers read before deciding
whether to trust it. The interface is therefore styled like tooling rather than
like a product launch: dense metadata, monospace for anything machine-derived,
near-square corners, 1px borders instead of shadows, no illustration, no
gradients, no marketing warmth. Restraint is the argument.

The monospace/sans split doubles as information architecture. IBM Plex Mono
means "this value came from the system" — identifiers, timestamps, IP addresses,
AAGUIDs, event ids, protocol names. Archivo means "a person wrote this for you".
A user who never consciously notices the rule still learns it.

## Colour

A near-monochrome warm-neutral ramp carries all structure. Hierarchy comes from
1px borders and two divider weights, not from fills or elevation.

Exactly two hues break the monochrome, and each has one job:

**Accent — teal.** Interactive affordance and product identity, and nothing
else. The primary passkey button, the brand mark, the ceremony halo, the
this-device and current-session dots, the rule beside the passkey explanation,
and focus rings. It never carries meaning about risk. Focus rings are accent
rather than warn precisely so that keyboard navigation never looks like an
alarm.

**Warning — amber-orange.** See [The one rule](#the-one-rule).

## Type

Scale, in pixels: 10, 11, 12, 13, 14, 15, 17, 20, 26, 28.

Body copy sits at 14–15px on a 1.6 line height. The passkey explanation on the
registration screen is promoted to 17px because it is the one sentence a
non-technical user has to read and understand. Eyebrow labels are 10–11px
monospace at 0.16em tracking.

## Spacing and radii

4px base unit. Container padding runs 20–26px; section rhythm 26–32px.

**Radii: square, with two exceptions.** Text inputs are 2px. The ceremony halo
and the status dots are circles. Buttons, cards, dialogs, and table rows have no
radius at all. The halo's circle is the only round form of any size in the
product, which is what makes that moment read as a different kind of event.

## The waiting state

The hardest screen in the product. The user is holding a finger on a sensor, and
any hint of failure makes them pull away — which causes the failure.

The treatment is deliberately un-spinner-like: two concentric rings and a
fingerprint glyph breathing on a 3.2s cycle at 1.28× scale with slow sinusoidal
easing. No rotation, no progress bar, no red, no countdown. The copy states the
deadline generously — "nothing expires for 60 seconds" — rather than showing it
tick away.

Verification is a separate state, not a continuation of waiting, because the
user's finger is off the sensor by then and the reassurance they need has
changed. It gets a short scanning bar and a checklist (`challenge ✓ · origin ✓ ·
counter …`) so that success reads as incremental rather than as a spinner that
might never stop.

Every transition writes one sentence into a polite, atomic live region. Without
it a screen reader user gets silence during exactly the pause that needs
narrating.

## Error copy

No message is reused across causes. An unrecognised credential, a dismissed
system prompt, and a 60-second timeout read differently because the user's next
action differs in each case. Cancellation says "nothing was sent" explicitly:
the fear after a half-finished ceremony is that something leaked.

## Dark mode

A parallel anchoring of the same roles, not an inversion or a filter. Surfaces
re-anchor near black, dividers tighten, and the accent lifts to stay legible on
a dark ground. The warning hue does **not** move — it must be recognisably the
same signal in both themes — while its text tint lightens where the light theme
darkens it.

Mechanically, dark mode is one attribute on `<html>` and nothing else. No
component reads the theme, and there is no dark-mode branch anywhere in the
TypeScript. The attribute selector is `[data-theme='dark']` rather than
`:root[data-theme='dark']`, so any element can open a theme scope — which is
what lets `/preview` paint both themes from one component tree.

### Known asymmetries

Two distinctions the light theme makes are collapsed in dark. Both are inherited
from the original design and kept rather than invented away:

- `--pl-muted` and `--pl-faint` resolve to the same value in dark. In light they
  are distinct, so a text hierarchy that exists in one theme does not exist in
  the other.
- `--pl-accent-strong` equals `--pl-accent` in dark. This one is defensible: the
  role exists to reach AA as small text, and the lifted accent already does on a
  near-black ground.

A third asymmetry is deliberate. `--pl-surface-sunk` is *darker* than
`--pl-surface` in light and *lighter* in dark. The name describes a recess, but
on a dark ground a recessed surface conventionally reads as lighter, so the
relationship flips.

## Responsive

Tables become stacked label/value cards below `md`. On the session list the
facts that matter — device, location, last activity, current-session marker —
stay visible at 380px, and the revoke control becomes a full-width 44px target.
Nothing hides behind a menu: someone hunting a suspicious session is usually on
the phone in their hand.

## Accessibility

- Every ceremony transition announces into a polite, atomic live region.
- Focus rings are a 2px accent outline at 3px offset, never removed.
- Severity in the activity log is encoded by shape (filled / hollow / accent
  square) **and** a text label, so colour is never the only carrier.
- Destructive confirmations are `role="alertdialog"`. The last-passkey case
  additionally requires typing `REMOVE` — the only typed confirmation in the
  product, reserved for the only action that can lock a user out permanently.
- Column labels in the passkey list are `md:sr-only`, not `md:hidden`.
  `display: none` would remove them from the accessibility tree on desktop and
  leave a screen reader announcing a bare date with no idea what it measures.

## Token reference

Three files, in this order:

| File | Role |
| --- | --- |
| [`tokens.css`](../client/src/design/tokens.css) | Palette and role tokens. The only place a colour is written. |
| [`theme.css`](../client/src/design/theme.css) | Maps role tokens onto Tailwind's namespaces. Introduces no values. |
| [`primitives.css`](../client/src/design/primitives.css) | Base element styles, two shared classes, keyframes. |

`theme.css` uses `@theme inline`, which is load-bearing rather than stylistic.
Without `inline`, Tailwind emits `--color-surface: var(--pl-surface)` once at
`:root`, pinning the value to the root theme; utilities would then ignore a
nested theme scope. With it, `.bg-surface` emits `var(--pl-surface)` directly and
resolves against whichever scope the element is in.

It also clears Tailwind's stock palette and type scale (`--color-*: initial`,
`--text-*: initial`). Off-system utilities such as `bg-red-500` then emit no CSS
at all, so the mistake surfaces as an unstyled element rather than as a
wrong-but-believable colour. This is not a build error, and it does not stop an
arbitrary value like `bg-[#ff0000]` — that is what the `check:colours` script
covers.

### Colour roles

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--pl-canvas` | `#F2F2EF` | `#0A0C0D` | Page ground |
| `--pl-surface` | `#FFFFFF` | `#111415` | Cards, dialogs, table rows |
| `--pl-surface-sunk` | `#F4F4F1` | `#16191A` | Inputs, dialog footers, current-session row |
| `--pl-line` | `#DEDED8` | `#23282A` | Container borders |
| `--pl-line-soft` | `#EDEDE8` | `#1B1F21` | Row dividers |
| `--pl-ink` | `#0C0E0F` | `#F2F4F3` | Primary text, dark button fills |
| `--pl-ink-2` | `#33393B` | `#D3D8D9` | Dialog body copy |
| `--pl-muted` | `#5B6265` | `#8B9497` | Secondary text, labels |
| `--pl-faint` | `#6E7477` | `#8B9497` | Monospace metadata |
| `--pl-accent` | `#00B8A9` | `#2FD4C4` | Affordance and identity only |
| `--pl-accent-strong` | `#00857A` | `#2FD4C4` | Accent as small text |
| `--pl-accent-edge` | `#009C90` | `#23A899` | 1px border under an accent fill |
| `--pl-accent-wash` | `#EAF7F5` | `#15302E` | Ceremony halo fill |
| `--pl-on-accent` | `#04231F` | `#04231F` | Text on an accent fill |
| `--pl-warn` | `#FF7A18` | `#FF7A18` | **Reserved.** Fill and 3px edge |
| `--pl-warn-text` | `#B84A00` | `#FF9B54` | **Reserved.** Warning label text |
| `--pl-warn-edge` | `#E06400` | `#FF7A18` | **Reserved.** 1px border under a warn fill |
| `--pl-on-warn` | `#231000` | `#231000` | Text on a warn fill |

### Type

| Token | Size | Utility | Use |
| --- | --- | --- | --- |
| `--pl-text-micro` | 10px | `text-micro` | Eyebrow labels, status dots' captions |
| `--pl-text-meta` | 11px | `text-meta` | Monospace metadata, brand mark |
| `--pl-text-caption` | 12px | `text-caption` | Field hints, timestamps |
| `--pl-text-fine` | 13px | `text-fine` | Inline monospace inside prose |
| `--pl-text-body` | 14px | `text-body` | Body copy |
| `--pl-text-body-lg` | 15px | `text-body-lg` | Row titles, primary buttons |
| `--pl-text-lead` | 17px | `text-lead` | Ceremony headings, the passkey explanation |
| `--pl-text-title` | 20px | `text-title` | Section and dialog headings |
| `--pl-text-display` | 26px | `text-display` | Sign-in heading |
| `--pl-text-display-lg` | 28px | `text-display-lg` | Registration heading |

### Spacing, radii, motion

| Token | Value | Notes |
| --- | --- | --- |
| `--pl-space-base` | `4px` | Drives Tailwind's whole spacing scale |
| `--pl-space-1` … `-9` | 4, 8, 12, 16, 20, 26, 32, 40, 56px | The rhythm the design uses |
| `--pl-radius-field` | `2px` | Text inputs only |
| `--pl-ceremony-duration` | `3200ms` | `0ms` under `prefers-reduced-motion` |
| `--pl-scan-duration` | `1400ms` | `0ms` under `prefers-reduced-motion` |
| `--pl-ease-calm` | `cubic-bezier(.37, 0, .63, 1)` | Sinusoidal; the breathing halo |
| `--pl-ease-scan` | `cubic-bezier(.4, 0, .2, 1)` | The verification bar |

Animations read their duration from these tokens rather than hardcoding it in a
utility class, so `prefers-reduced-motion` has exactly one place to act.

## Preview

`/preview` renders every base component twice — once in a light scope, once in a
dark scope — from a single component tree, alongside controls that drive the
ceremony through each of its states.

Deliberately not Storybook. The system's central claim is that a component
cannot tell which theme it is in, and the cheapest way to demonstrate that is to
paint both at once from the same instances. A separate tool would demonstrate it
about a separate build.

## Deviations from the design session

Recorded because the design output and this implementation disagree in a few
places, and the disagreements were decisions rather than drift:

| Point | Design output | Here |
| --- | --- | --- |
| Warn on borders | `tokens.css` said "never a border"; the canvas and `DESIGN.md` both used one | Permitted as a 3px dialog edge and a 1px edge under a fill; forbidden elsewhere |
| `--pl-canvas` | `#F2F2EF` in tokens; the canvas's own token board labelled it `#0A0C0D` | `#F2F2EF` light, `#0A0C0D` dark |
| Input background | `#FBFBF9` in tokens; `#F4F4F1` in the canvas | `#F4F4F1` — `#FBFBF9` is invisible against white |
| Section headings | 18px and 19px in the canvas, neither on the declared scale | Folded into the declared 20px |
| Radii | "2px throughout", but only inputs ever had it | Square, except inputs and circles |
| Tokens | Type, spacing, radii and motion tokens defined but unconsumed | All mapped through `theme.css` and used |
