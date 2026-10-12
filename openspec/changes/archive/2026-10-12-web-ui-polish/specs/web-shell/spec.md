# Spec Delta

## MODIFIED Requirements

### Requirement: Design system
Views SHALL be built from a shared component library whose colors, type, spacing, radii and elevation come from design tokens. Every token SHALL have a light and a dark value, following the system color scheme. Fonts and icons SHALL be served from the same origin so the Content Security Policy stays unchanged. Every interactive control SHALL have a visible keyboard focus state and a touch target of at least 44 × 44 px. Controls placed side by side in one row (inputs, buttons, chips, the first line of a multi-line input) SHALL share one vertical centre. Single-line inputs and the buttons next to them SHALL use the shared control heights.

#### Scenario: Dark mode
- **WHEN** the system color scheme is dark
- **THEN** every view uses the dark token values and no text is rendered with a light-theme color on a dark surface

#### Scenario: Keyboard focus
- **WHEN** a user moves through a view with the Tab key
- **THEN** every focused control shows a visible focus indicator

#### Scenario: One-line chat draft
- **WHEN** the chat composer holds an empty or one-line draft
- **THEN** the placeholder or text line and the send button share one vertical centre

#### Scenario: Multi-line chat draft
- **WHEN** the chat draft grows to several lines
- **THEN** the send button stays at the bottom edge of the composer
