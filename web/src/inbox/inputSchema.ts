/** One field of an MCP input request form, read from the requested schema (string, number, integer, boolean, enum). */
export interface InputField {
  name: string
  label: string
  description?: string
  kind: 'text' | 'number' | 'boolean' | 'choice'
  integer: boolean
  required: boolean
  options: { value: string; label: string }[]
  initial: string | number | boolean | null
}

interface PropertySchema {
  type?: string
  title?: string
  description?: string
  enum?: unknown[]
  enumNames?: unknown[]
  oneOf?: { const?: unknown; title?: unknown }[]
  default?: unknown
}

/** The form's fields in schema order; unknown property types become text fields. */
export function inputFields(schema: unknown): InputField[] {
  const object = (schema ?? {}) as { properties?: Record<string, PropertySchema>; required?: string[] }
  const required = new Set(object.required ?? [])
  return Object.entries(object.properties ?? {}).map(([name, property]) => {
    const options = property.oneOf
      ? property.oneOf.map((option) => ({ value: String(option.const), label: String(option.title ?? option.const) }))
      : property.enum
        ? property.enum.map((value, index) => ({ value: String(value), label: String(property.enumNames?.[index] ?? value) }))
        : []
    const kind = options.length ? 'choice' : property.type === 'boolean' ? 'boolean' : property.type === 'number' || property.type === 'integer' ? 'number' : 'text'
    const fallback = kind === 'boolean' ? false : kind === 'choice' ? (options[0]?.value ?? '') : kind === 'number' ? null : ''
    const initial = typeof property.default === 'string' || typeof property.default === 'number' || typeof property.default === 'boolean' ? property.default : fallback
    return {
      name,
      label: property.title ?? name,
      description: property.description,
      kind,
      integer: property.type === 'integer',
      required: required.has(name),
      options,
      initial: kind === 'choice' && initial !== null ? String(initial) : initial,
    }
  })
}

/** The answer object: numbers as numbers, empty optional fields left out. */
export function inputAnswer(fields: InputField[], values: Record<string, string | number | boolean | null>): Record<string, unknown> {
  const answer: Record<string, unknown> = {}
  for (const field of fields) {
    const value = values[field.name]
    if (value === null || value === '' || value === undefined) {
      if (field.kind === 'boolean') answer[field.name] = false
      continue
    }
    answer[field.name] = field.kind === 'number' ? (field.integer ? Math.trunc(Number(value)) : Number(value)) : value
  }
  return answer
}
