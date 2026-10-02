## MODIFIED Requirements

### Requirement: Responsive layout
Every view SHALL have a designed phone layout and a designed desktop layout; a phone layout SHALL NOT be only the desktop layout reflowed into one column. The views people act on (dashboard, approvals and review, chat, notifications, printer, task results, kill switch) SHALL be designed phone-first. The views people manage (memory graph, scatter and editor, runs, cost, configuration, mail accounts, security, secrets) SHALL be designed desktop-first and MAY offer a reduced phone layout. Every view SHALL be usable at 375 px width without horizontal scrolling of the page.

#### Scenario: Phone width
- **WHEN** the chat view is rendered at 375 px width
- **THEN** there is no horizontal scrolling and all actions are reachable

#### Scenario: Approval from a push notification on a phone
- **WHEN** a push notification for a pending approval is opened on a phone
- **THEN** a focused approval screen shows the frozen action, its arguments and the time left, with approve and reject reachable without scrolling

#### Scenario: Manage view on a phone
- **WHEN** the memory graph is opened at 375 px width
- **THEN** a phone layout is shown (for example a list of the same chunks) instead of a shrunken desktop graph

## ADDED Requirements

### Requirement: Design system
Views SHALL be built from a shared component library whose colors, type, spacing, radii and elevation come from design tokens. Every token SHALL have a light and a dark value, following the system color scheme. Fonts and icons SHALL be served from the same origin so the Content Security Policy stays unchanged. Every interactive control SHALL have a visible keyboard focus state and a touch target of at least 44 × 44 px.

#### Scenario: Dark mode
- **WHEN** the system color scheme is dark
- **THEN** every view uses the dark token values and no text is rendered with a light-theme color on a dark surface

#### Scenario: Keyboard focus
- **WHEN** a user moves through a view with the Tab key
- **THEN** every focused control shows a visible focus indicator

### Requirement: Navigation
On phones the shell SHALL show a bottom tab bar with at most five destinations, the last of which opens the remaining destinations. On desktop the shell SHALL show a grouped side navigation. Destinations that need attention (pending approvals, open proposals, unread critical notifications) SHALL show a count badge in both layouts. Admin-only destinations SHALL be hidden from users without the `admin` role.

#### Scenario: Pending approval badge
- **WHEN** two approvals are pending
- **THEN** the destination that leads to approvals shows the count 2

#### Scenario: Non-admin user
- **WHEN** a user without the `admin` role opens the navigation
- **THEN** the configuration view is not listed

### Requirement: API error presentation
Every API call from the SPA SHALL handle failure; no failure SHALL leave the view blank or unchanged without a message. Failures SHALL be classified as network (no response), problem (an error status, with the ProblemDetails `code` and `detail` when present) or unexpected (anything thrown while preparing or reading the request). The message shown SHALL be in the interface language: a translation for a known problem `code` when one exists, otherwise the server's `detail`, otherwise a generic message for the status or class. A failed form submission SHALL show the message next to the form and keep the entered values. A failed load SHALL show a "could not load" state with a retry action instead of empty content. A 401 from any call after sign-in SHALL return to the login page with a "session expired" message and the current path as return URL. Errors no view handles SHALL be shown in a global banner. The trace id of an unexpected server error SHALL be shown so it can be matched with the server log.

#### Scenario: Setup over plain HTTP
- **WHEN** the owner submits the first-run setup while the app is opened over plain HTTP
- **THEN** the setup form shows a message that Marshal must be opened over HTTPS, and the entered user name stays in the form

#### Scenario: Network failure while loading
- **WHEN** the tasks view loads and the API is unreachable
- **THEN** the view shows that Marshal could not be reached and offers a retry, instead of an empty task list

#### Scenario: Failure of the session check
- **WHEN** `GET /api/v1/me` fails with a status other than 401 at app start
- **THEN** a full-page error with a retry is shown instead of a blank page

#### Scenario: Session expired
- **WHEN** a signed-in user's cookie has expired and a view's request returns 401
- **THEN** the login page opens with a "session expired" message and returns to that view after sign-in

#### Scenario: Known problem code
- **WHEN** a request fails with the problem code `auth.https_required` and the interface language is German
- **THEN** the German message for that code is shown, not the English server `detail`

### Requirement: Interface language and formatting
All interface text SHALL be available in German and English. Signed in, the interface language and time zone SHALL be the user's preferences (`/api/v1/me/preferences`) and SHALL change without a reload when the user updates them. Before sign-in, the interface language SHALL follow the browser, falling back to English. Dates, times, numbers and durations SHALL be formatted for the interface language and the user's time zone. Content written by people or the model (memory chunks, chat messages, notifications) SHALL be shown as stored, not translated.

#### Scenario: German preference
- **WHEN** a user whose language preference is `de` signs in
- **THEN** the navigation, view titles, buttons and messages are German and dates use German formatting

#### Scenario: Language change
- **WHEN** the user changes the language preference from `en` to `de` in the preferences view
- **THEN** the interface switches to German without a page reload

#### Scenario: Login page before sign-in
- **WHEN** a browser whose preferred language is German opens the login page
- **THEN** the login page is German

### Requirement: Preview mode
The SPA SHALL provide a preview mode (`npm run dev:preview`) that runs the full app against a built-in fake API with sample data, signed in, without the .NET host. Preview mode SHALL offer scenarios at least for normal data, empty data, failing requests and an unreachable API, and SHALL let the interface language be switched. Preview mode SHALL NOT be included in the production build.

#### Scenario: Error scenario
- **WHEN** preview mode runs with the failing-requests scenario
- **THEN** every view shows its error state

#### Scenario: Production build
- **WHEN** `npm run build` produces the production bundle
- **THEN** the bundle contains no fake API and no sample data

### Requirement: Installable app
The SPA SHALL ship a web app manifest and a service worker so that it can be installed to a phone's home screen and opens without browser chrome. The service worker SHALL cache only the app shell (HTML, scripts, styles, fonts, icons) and SHALL NOT cache API responses or the live-event stream. A new version SHALL be picked up on the next start without the user clearing caches. Without a connection the installed app SHALL show that Marshal cannot be reached.

#### Scenario: API never cached
- **WHEN** the installed app is offline and a view requests `/api/v1/tasks`
- **THEN** the request fails as a network error and is not answered from a cache

#### Scenario: Update
- **WHEN** a new version of Marshal is deployed and the installed app is started again
- **THEN** the new version is shown
