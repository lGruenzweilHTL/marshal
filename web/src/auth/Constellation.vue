<script setup lang="ts">
import { onBeforeUnmount, onMounted, shallowRef } from 'vue'

/**
 * The sign-in panel's backdrop: points drifting slowly, lines joining near neighbours. Decorative only; it stands still
 * when the user prefers reduced motion. Colours come from the `--panel-*` tokens.
 */
const props = withDefaults(defineProps<{ count?: number; reach?: number; speed?: number }>(), { count: 110, reach: 140, speed: 0.35 })

const width = 720
const height = 900
type Kind = 'accent' | 'info' | 'node'
interface Point {
  x: number
  y: number
  vx: number
  vy: number
  kind: Kind
}

let seed = 20261002
const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647
const points: Point[] = Array.from({ length: props.count }, (_, index) => ({
  x: random() * width,
  y: random() * height,
  vx: random() - 0.5,
  vy: random() - 0.5,
  kind: index % 12 === 0 ? 'accent' : index % 9 === 0 ? 'info' : 'node',
}))

const frame = shallowRef(render())

function render() {
  const lines: { key: string; x1: number; y1: number; x2: number; y2: number; hot: boolean; opacity: number }[] = []
  for (let i = 0; i < points.length; i++) {
    for (let j = i + 1; j < points.length; j++) {
      const a = points[i]!
      const b = points[j]!
      const distance = Math.hypot(a.x - b.x, a.y - b.y)
      if (distance >= props.reach) continue
      const hot = a.kind === 'accent' || b.kind === 'accent'
      lines.push({ key: `${i}-${j}`, x1: a.x, y1: a.y, x2: b.x, y2: b.y, hot, opacity: (1 - distance / props.reach) * (hot ? 0.7 : 0.35) })
    }
  }
  return { lines, points: points.map((point, index) => ({ key: index, x: point.x, y: point.y, kind: point.kind })) }
}

let handle = 0
let last = 0
function step(time: number) {
  handle = requestAnimationFrame(step)
  if (time - last < 33) return
  last = time
  for (const point of points) {
    point.x += point.vx * props.speed
    point.y += point.vy * props.speed
    if (point.x < -20 || point.x > width + 20) point.vx *= -1
    if (point.y < -20 || point.y > height + 20) point.vy *= -1
  }
  frame.value = render()
}

onMounted(() => {
  if (typeof requestAnimationFrame !== 'function' || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return
  handle = requestAnimationFrame(step)
})
onBeforeUnmount(() => cancelAnimationFrame(handle))
</script>

<template>
  <div class="constellation" aria-hidden="true">
    <svg :viewBox="`0 0 ${width} ${height}`" width="100%" height="100%" preserveAspectRatio="xMidYMid slice">
      <line
        v-for="line in frame.lines"
        :key="line.key"
        :x1="line.x1"
        :y1="line.y1"
        :x2="line.x2"
        :y2="line.y2"
        :class="{ hot: line.hot }"
        :stroke-opacity="line.opacity.toFixed(2)"
      />
      <circle v-for="point in frame.points" :key="point.key" :cx="point.x" :cy="point.y" :r="point.kind === 'node' ? 1.8 : 3.4" :class="point.kind" />
    </svg>
  </div>
</template>

<style scoped>
.constellation {
  position: absolute;
  inset: 0;
  overflow: hidden;
  background: radial-gradient(120% 90% at 70% 35%, var(--panel-glow) 0%, var(--panel-bg) 60%);
}
line {
  stroke: var(--panel-node);
  stroke-width: 1;
}
line.hot {
  stroke: var(--panel-accent);
}
circle {
  fill: var(--panel-node);
}
circle.accent {
  fill: var(--panel-accent);
}
circle.info {
  fill: var(--panel-info);
}
</style>
