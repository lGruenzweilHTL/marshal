import type { paths } from '../../api/schema'

type Json<R> = R extends { content: { 'application/json': infer T } } ? T : never

/** API paths with a JSON GET response. */
export type GetPath = {
  [P in keyof paths]: paths[P] extends { get: { responses: { 200: { content: { 'application/json': unknown } } } } } ? P : never
}[keyof paths]

/** The 200 body of a GET path. */
export type GetData<P extends GetPath> = paths[P] extends { get: { responses: { 200: infer R } } } ? Json<R> : never

/** Sample bodies per GET path; paths may contain `{name}` segments. Typed against the OpenAPI schema. */
export type Fixtures = { [P in GetPath]?: GetData<P> }
