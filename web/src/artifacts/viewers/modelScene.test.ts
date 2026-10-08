// @vitest-environment jsdom
import { Mesh, type Object3D } from 'three'
import { zipSync, strToU8 } from 'three/examples/jsm/libs/fflate.module.js'
import { describe, expect, it } from 'vitest'
import { parseModel } from './modelScene'

function meshes(root: Object3D): Mesh[] {
  const found: Mesh[] = []
  root.traverse((node) => {
    if (node instanceof Mesh) found.push(node)
  })
  return found
}

const ascii = 'solid cube\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid cube\n'

function binaryStl(): ArrayBuffer {
  const buffer = new ArrayBuffer(84 + 50)
  const view = new DataView(buffer)
  view.setUint32(80, 1, true)
  const vertices = [0, 0, 0, 1, 0, 0, 0, 1, 0]
  vertices.forEach((value, index) => view.setFloat32(84 + 12 + index * 4, value, true))
  return buffer
}

function threeMf(): ArrayBuffer {
  const model = `<?xml version="1.0" encoding="UTF-8"?>
<model unit="millimeter" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
  <resources>
    <object id="1" type="model"><mesh>
      <vertices><vertex x="0" y="0" z="0"/><vertex x="10" y="0" z="0"/><vertex x="0" y="10" z="0"/></vertices>
      <triangles><triangle v1="0" v2="1" v3="2"/></triangles>
    </mesh></object>
  </resources>
  <build><item objectid="1"/></build>
</model>`
  const rels = `<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>`
  // jsdom's TextEncoder returns another realm's Uint8Array, which fflate would take for a folder.
  const bytes = (text: string) => new Uint8Array(strToU8(text))
  const zipped = zipSync({ '_rels/.rels': bytes(rels), '3D/3dmodel.model': bytes(model) })
  return zipped.buffer.slice(zipped.byteOffset, zipped.byteOffset + zipped.byteLength) as ArrayBuffer
}

describe('parseModel', () => {
  it('reads ASCII and binary STL into one mesh', () => {
    for (const content of [new TextEncoder().encode(ascii).buffer as ArrayBuffer, binaryStl()]) {
      const [mesh] = meshes(parseModel('cube.stl', content))
      expect(mesh!.geometry.getAttribute('position').count).toBe(3)
    }
  })

  it('reads a 3MF package', () => {
    const found = meshes(parseModel('bracket.3mf', threeMf()))
    expect(found).toHaveLength(1)
    expect(found[0]!.geometry.getAttribute('position').count).toBe(3)
  })
})
