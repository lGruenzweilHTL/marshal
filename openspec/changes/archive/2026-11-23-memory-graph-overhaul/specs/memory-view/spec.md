# Spec Delta

## ADDED Requirements

### Requirement: Chunk neighbourhood API
The API SHALL provide an owner-scoped endpoint that returns a live chunk, its live direct neighbours (chunks linked to it by an edge in either direction) and the edges between any two of these chunks. It SHALL answer not found for a chunk that does not exist, is soft-deleted or belongs to another owner.

#### Scenario: Neighbours in both directions
- **WHEN** chunk A depends on B, and C supersedes A
- **THEN** the neighbourhood of A contains A, B and C and both edges

#### Scenario: Edges between neighbours
- **WHEN** B and C are both neighbours of A and B relates to C
- **THEN** the neighbourhood of A also contains the edge between B and C

#### Scenario: Deleted neighbour
- **WHEN** a neighbour of A is soft-deleted
- **THEN** the neighbourhood of A leaves out that chunk and its edges

#### Scenario: Unknown chunk
- **WHEN** the neighbourhood of a soft-deleted chunk or another owner's chunk is requested
- **THEN** the API answers not found

### Requirement: Graph navigation
The memory graph SHALL let the user zoom with the wheel and with on-screen controls, pan by dragging the background, fit the whole graph into view, and drag a node, after which the layout adjusts around it. Clicking a node or pressing Enter on it SHALL open its chunk.

#### Scenario: Fit to view
- **WHEN** the user has zoomed in and panned away, then chooses fit to view
- **THEN** every visible chunk is inside the drawing

#### Scenario: Drag a node
- **WHEN** the user drags a node and releases it
- **THEN** the node and its linked nodes move and the chunk does not open

### Requirement: Graph highlight
When the user hovers over or keyboard-focuses a node in a memory graph, that node, its direct neighbours and the edges between them SHALL be emphasised and all other nodes and edges SHALL be dimmed but stay visible. Leaving the node SHALL restore the normal drawing.

#### Scenario: Hover a node
- **WHEN** the user hovers a node with two linked neighbours
- **THEN** those three nodes and their edges are emphasised and the rest is dimmed

#### Scenario: Keyboard focus
- **WHEN** the user moves keyboard focus to a node
- **THEN** the same highlight is shown as on hover

### Requirement: Graph labels
At the zoom that fits the whole graph, a memory graph SHALL label pinned chunks and at most the ten chunks with the most links. Zooming in SHALL label more chunks, first those of higher importance or with more links, until every chunk is labelled. A hovered or focused node and its direct neighbours SHALL always be labelled. Labels SHALL stay inside the drawing.

#### Scenario: Fit zoom on a large memory
- **WHEN** a graph of 80 chunks, two of them pinned, is shown at fit zoom
- **THEN** at most twelve labels are visible, including both pinned chunks

#### Scenario: Zoomed in
- **WHEN** the user zooms in far enough
- **THEN** every chunk in view is labelled

#### Scenario: Hovered node always labelled
- **WHEN** the user hovers an unlabelled node at fit zoom
- **THEN** the node and its direct neighbours are labelled

### Requirement: Graph appearance
A memory graph SHALL colour nodes by chunk type, size them by importance and number of links, and mark pinned chunks. It SHALL show the direction of `dependsOn` and `supersedes` edges, and show `contradicts` edges differently from all other kinds. It SHALL work in light and dark appearance. Any settle animation SHALL be skipped when the user prefers reduced motion.

#### Scenario: Directed edge
- **WHEN** chunk A depends on chunk B
- **THEN** the edge between them shows that it points from A to B

#### Scenario: Reduced motion
- **WHEN** the user prefers reduced motion and opens the graph
- **THEN** the graph appears in its final layout without animation

### Requirement: Graph type filters
The legend of the memory map's graph SHALL let the user hide and show the chunks of each type. A hidden type's chunks and their edges SHALL be removed from the graph, not dimmed, and fit to view SHALL consider only shown chunks. At least one type SHALL stay shown.

#### Scenario: Hide a type
- **WHEN** the user turns off the `event` type in the legend
- **THEN** no event chunk and none of their edges are drawn, and the legend shows `event` as off

#### Scenario: Last shown type
- **WHEN** only one type is shown
- **THEN** that type cannot be turned off

### Requirement: Graph project grouping
The memory map's graph SHALL place the chunks of each project near each other and draw a neutral backdrop with the project's name behind each project's chunks. Chunks without a project SHALL not get a backdrop. Project grouping SHALL not use colour, so that colour keeps meaning chunk type only.

#### Scenario: Two projects
- **WHEN** the graph shows chunks of two projects and chunks without a project
- **THEN** each project's chunks lie on one backdrop labelled with the project name, and chunks without a project lie on none

### Requirement: Local graph in the chunk editor
The chunk editor SHALL show a graph of the chunk's neighbourhood above its list of links, with the edited chunk marked as the centre. It SHALL be shown at every width, including phones, without horizontal page scroll, SHALL offer the same highlight and labels as the memory graph, and SHALL be left out when the chunk has no links.

#### Scenario: Chunk with links
- **WHEN** the user opens a chunk with three links
- **THEN** the editor shows the chunk at the centre and its three neighbours, and clicking a neighbour opens that chunk

#### Scenario: Chunk without links
- **WHEN** the user opens a chunk without links
- **THEN** no local graph is shown

#### Scenario: Local graph on a phone
- **WHEN** a chunk with links is opened at phone width
- **THEN** the local graph is shown inside the page width without horizontal page scroll
