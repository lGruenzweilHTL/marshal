# Diagrams

| Diagram | Source | Rendered |
|---|---|---|
| Entity relationship diagram | [`erd.puml`](erd.puml) | [`erd.svg`](erd.svg) |
| Package diagram | [`packages.puml`](packages.puml) | [`packages.svg`](packages.svg) |

## Keep them up to date

The diagrams must always match the code:

- **ERD:** update `erd.puml` in the same change that adds, removes or renames an EF Core entity, column or relationship.
- **Package diagram:** update `packages.puml` in the same change that adds, removes or renames a project in `src/`, `web/` or `ha/`, or changes a project reference or key external dependency.
- **Always re-render the SVGs** after editing a `.puml` file and commit both together. The README embeds the SVGs.
- While a diagram describes planned rather than implemented structure, its title says "(planned)". Remove that marker once the code exists.

## Rendering

The diagrams use PlantUML's built-in `smetana` layout, so Graphviz is not needed. Only Java or Docker is required.

With Java and the PlantUML jar ([download](https://github.com/plantuml/plantuml/releases/latest)):

```sh
java -jar plantuml.jar -tsvg docs/diagrams/*.puml
```

With Docker:

```sh
docker run --rm -v "$PWD":/work -w /work plantuml/plantuml -tsvg docs/diagrams/*.puml
```

Editor previews: the PlantUML extensions for VS Code and JetBrains IDEs render `.puml` files directly.
