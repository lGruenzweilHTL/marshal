import { Box, File, FileCode, FileDiff, FileText, Film, Globe, Image, Shapes } from 'lucide-vue-next'
import type { Component } from 'vue'
import type { Schemas } from '../api/client'

export type Artifact = Schemas['ArtifactResponse']
export type ArtifactKind = Schemas['ArtifactKind']
export type ArtifactSource = Schemas['ArtifactSource']

export const kinds: ArtifactKind[] = ['image', 'svg', 'video', 'model3d', 'code', 'diff', 'pdf', 'markdown', 'html', 'other']
export const sources: ArtifactSource[] = ['upload', 'executor', 'printer', 'mail', 'github']

export const kindIcons: Record<ArtifactKind, Component> = {
  image: Image,
  svg: Shapes,
  video: Film,
  model3d: Box,
  code: FileCode,
  diff: FileDiff,
  pdf: FileText,
  markdown: FileText,
  html: Globe,
  other: File,
}

/** Kinds the server renders a preview image for (`GET /api/v1/artifacts/{id}/preview`). */
const previewKinds: ReadonlySet<ArtifactKind> = new Set(['model3d', 'image'])

/** The preview image next to the artifact's content URL, or undefined for kinds without one. */
export function previewUrl(artifact: Artifact): string | undefined {
  return previewKinds.has(artifact.kind) ? artifact.url.replace(/\/content$/, '/preview') : undefined
}
