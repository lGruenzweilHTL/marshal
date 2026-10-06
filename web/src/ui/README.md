# Design system in code

`web/src/ui/` holds the design system of the "Marshal Redesign" canvas: tokens, base styles, fonts, the logo and the
components every view is built from.

## Rules

- Colours, fonts, spacing, radii, shadows and type styles come from CSS custom properties in `tokens.css` (and the
  layout variables in `base.css`). Components never contain literal colours or font families.
- Views use the components exported from `index.ts`; a new pattern that appears twice becomes a component here.
- Every interactive control has a visible focus state and a 44 px target (`--target`).

## Updating the tokens

`tokens.json` is an export of the "Marshal" design system (https://claude.ai/artifact/DXfMfWe5jKksRwqjNjEFAJ),
extended with app-only tokens: the logo colours (`logo-*`), the sign-in panel (`panel*`, `constellation-*`),
`mem-event` and the dark shadow values. The same steps are on the canvas page Foundations › Token export.

1. Download `project/tokens.json` from the design system and merge it into `tokens.json` (keep the extra tokens).
2. Run `npm run gen:tokens`; it rewrites `tokens.css`. Never edit `tokens.css` by hand.
3. Check both themes in preview mode (`npm run dev:preview`, appearance switch in Preferences).

## Replacing the logo

The logo exists only as SVG files in `logo/`, and only `logo/Logo.vue` renders them:

| File | Used for |
|---|---|
| `wordmark.svg` | The name in the navigation and on the sign-in pages. Letters use `currentColor`, the accent `var(--logo-accent)`. |
| `mark.svg` | The mark without a tile (chat avatar). Uses `var(--logo-mark)`. |
| `tile.svg` | The mark on its tile (app bar, small places). Uses `var(--logo-tile)` and `var(--logo-mark)`. |
| `icon.svg`, `icon-maskable.svg` | Favicon and PWA icons (fixed colours, no CSS variables, maskable keeps a 10 % safe margin). |

To change the logo, replace these files (keep the file names and use the CSS variables where the table says so), adjust
the logo colour tokens in `tokens.json` if needed and run `npm run gen:tokens` and `npm run build` (the build renders
the PNG icons from `icon.svg` and `icon-maskable.svg`).
