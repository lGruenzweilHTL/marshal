import { Box3, BufferGeometry, DirectionalLight, Group, HemisphereLight, Mesh, MeshStandardMaterial, Object3D, PerspectiveCamera, Scene, Vector3, WebGLRenderer } from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { ThreeMFLoader } from 'three/examples/jsm/loaders/3MFLoader.js'
import { STLLoader } from 'three/examples/jsm/loaders/STLLoader.js'

// Only this module imports three.js; the 3D viewer loads it lazily, so it stays out of the main chunk.

/** The model in an STL or 3MF file (by extension); STL gets a neutral material. */
export function parseModel(name: string, content: ArrayBuffer): Object3D {
  if (name.toLowerCase().endsWith('.3mf')) return new ThreeMFLoader().parse(content)
  const geometry: BufferGeometry = new STLLoader().parse(content)
  geometry.computeVertexNormals()
  return new Mesh(geometry, new MeshStandardMaterial({ color: 0x9aa4b2, roughness: 0.6, metalness: 0.1 }))
}

export interface ModelStage {
  dispose(): void
}

/** Renders the model centred and scaled into the canvas with orbit controls (drag rotates, wheel zooms). */
export function stage(canvas: HTMLCanvasElement, model: Object3D): ModelStage {
  const renderer = new WebGLRenderer({ canvas, antialias: true, alpha: true })
  renderer.setPixelRatio(window.devicePixelRatio)
  const scene = new Scene()
  scene.add(new HemisphereLight(0xffffff, 0x444444, 2.2))
  const light = new DirectionalLight(0xffffff, 1.5)
  light.position.set(1, 2, 3)
  scene.add(light)

  // 3MF and many STL files are Z-up; three.js is Y-up.
  const group = new Group()
  group.add(model)
  group.rotation.x = -Math.PI / 2
  scene.add(group)
  const bounds = new Box3().setFromObject(group)
  const size = bounds.getSize(new Vector3()).length() || 1
  group.position.sub(bounds.getCenter(new Vector3()))

  const camera = new PerspectiveCamera(40, 1, size / 100, size * 100)
  camera.position.set(size * 0.8, size * 0.6, size * 0.9)
  const controls = new OrbitControls(camera, canvas)
  controls.enableDamping = true

  let frame = 0
  const render = () => {
    const { clientWidth: width, clientHeight: height } = canvas
    if (canvas.width !== width || canvas.height !== height) {
      renderer.setSize(width, height, false)
      camera.aspect = width / Math.max(1, height)
      camera.updateProjectionMatrix()
    }
    controls.update()
    renderer.render(scene, camera)
    frame = requestAnimationFrame(render)
  }
  render()
  return {
    dispose() {
      cancelAnimationFrame(frame)
      controls.dispose()
      renderer.dispose()
    },
  }
}
