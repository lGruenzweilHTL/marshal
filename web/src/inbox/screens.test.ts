import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ApprovalView from './ApprovalView.vue'
import NotificationView from './NotificationView.vue'
import ProposalView from './ProposalView.vue'
import SkillProposalView from './SkillProposalView.vue'
import { FakeServer } from '../testing/fakeServer'
import { approvalItem, notificationEntry, proposal, skillProposal } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

const future = new Date(Date.now() + 5 * 60_000).toISOString()
const diff = 'diff --git a/feature.txt b/feature.txt\n--- /dev/null\n+++ b/feature.txt\n@@ -0,0 +1 @@\n-old line\n+change for demo\n+second line\n'

function server() {
  return new FakeServer().on('GET', '/api/v1/me', 200, me).antiforgery('xsrf')
}

async function mountView(component: object, fake: FakeServer, path: string) {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(component, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('ApprovalView', () => {
  it('shows the full ticket, who can approve and where the approval came from', async () => {
    const fake = server().on('GET', '/api/v1/approvals', 200, [approvalItem({ expiresAt: future })])
    const { wrapper } = await mountView(ApprovalView, fake, '/inbox/approvals/a-1')

    expect(wrapper.get('[data-approval="a-1"]').text()).toContain('"door": "front"')
    expect(wrapper.text()).toContain('Only you can approve')
    expect(wrapper.find('a[href="/tasks/t-1"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/runs/r-1"]').exists()).toBe(true)
  })

  it('says so when the approval is no longer pending', async () => {
    const fake = server().on('GET', '/api/v1/approvals', 200, [])
    const { wrapper } = await mountView(ApprovalView, fake, '/inbox/approvals/a-1')

    expect(wrapper.text()).toContain('already decided or has expired')
  })
})

describe('ProposalView', () => {
  it('shows the diff with its file and line counts', async () => {
    const fake = server().on('GET', '/api/v1/proposals/p-1', 200, proposal({ diff }))
    const { wrapper } = await mountView(ProposalView, fake, '/inbox/proposals/p-1')

    expect(wrapper.text()).toContain('Add a feature file')
    expect(wrapper.text()).toContain('1 file')
    expect(wrapper.get('[data-test="stat"]').text()).toBe('+2 −1')
    expect(wrapper.findAll('.added')).toHaveLength(2)
    expect(wrapper.findAll('.removed')).toHaveLength(1)
  })

  it('merges and shows the result', async () => {
    const fake = server()
      .on('GET', '/api/v1/proposals/p-1', 200, proposal({ diff }))
      .on('POST', '/api/v1/proposals/p-1/merge', 200, proposal({ diff, status: 'merged' }))
    const { wrapper } = await mountView(ProposalView, fake, '/inbox/proposals/p-1')

    await wrapper.get('[data-test="merge"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Merged.')
    expect(wrapper.find('[data-test="merge"]').exists()).toBe(false)
  })

  it('keeps a merge conflict on screen with the actions', async () => {
    const fake = server()
      .on('GET', '/api/v1/proposals/p-1', 200, proposal({ diff }))
      .on('POST', '/api/v1/proposals/p-1/merge', 409, { status: 409, code: 'proposal.conflict', detail: 'The branch no longer merges cleanly.' })
    const { wrapper } = await mountView(ProposalView, fake, '/inbox/proposals/p-1')

    await wrapper.get('[data-test="merge"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('no longer merges cleanly')
    expect(wrapper.find('[data-test="merge"]').exists()).toBe(true)
  })

  it('shows the error when the proposal cannot be loaded', async () => {
    const fake = server().on('GET', '/api/v1/proposals/p-1', 404, { status: 404, code: 'proposal.not_found', detail: 'There is no such proposal.' })
    const { wrapper } = await mountView(ProposalView, fake, '/inbox/proposals/p-1')

    expect(wrapper.text()).toContain('This proposal no longer exists.')
  })
})

describe('NotificationView', () => {
  it('shows the text, priority, delivery and the source task', async () => {
    const fake = server().on('GET', '/api/v1/notifications', 200, [notificationEntry({ priority: 'critical', reason: 'quiet hours' })])
    const { wrapper } = await mountView(NotificationView, fake, '/inbox/notifications/n-1')

    expect(wrapper.text()).toContain('Two new mails.')
    expect(wrapper.text()).toContain('critical')
    expect(wrapper.text()).toContain('delivered')
    expect(wrapper.text()).toContain('quiet hours')
    expect(wrapper.find('a[href="/tasks/t-1"]').exists()).toBe(true)
  })

  it('says so when the notification is not in the list', async () => {
    const fake = server().on('GET', '/api/v1/notifications', 200, [])
    const { wrapper } = await mountView(NotificationView, fake, '/inbox/notifications/n-1')

    expect(wrapper.text()).toContain('no longer in the list')
  })
})

describe('SkillProposalView', () => {
  it('previews the drafted SKILL.md, the diff and the source runs, and offers the download', async () => {
    const fake = server().on('GET', '/api/v1/skill-proposals/sp-1', 200, skillProposal({ diff: '--- a/SKILL.md\n+++ b/SKILL.md\n@@ -1 +1 @@\n-old\n+new\n' }))
    const { wrapper } = await mountView(SkillProposalView, fake, '/inbox/skills/sp-1')

    expect(wrapper.get('[data-test="content"]').text()).toContain('name: nightly-backup-check')
    expect(wrapper.get('[data-test="diff"]').text()).toContain('+new')
    expect(wrapper.find('a[href="/runs/r-3"]').exists()).toBe(true)
    const download = wrapper.get('[data-test="download"]')
    expect(download.attributes('href')).toMatch(/\/api\/v1\/skill-proposals\/sp-1\/download$/)
    expect(download.attributes('download')).toBe('SKILL.md')
  })

  it('tells the user where the file goes after the download', async () => {
    const fake = server().on('GET', '/api/v1/skill-proposals/sp-1', 200, skillProposal())
    const { wrapper } = await mountView(SkillProposalView, fake, '/inbox/skills/sp-1')

    await wrapper.get('[data-test="download"]').trigger('click')

    expect(wrapper.get('[data-test="install"]').text()).toContain('/config/skills/nightly-backup-check/SKILL.md')
    expect(wrapper.text()).toContain('Downloaded')
  })

  it('dismisses the proposal', async () => {
    const fake = server()
      .on('GET', '/api/v1/skill-proposals/sp-1', 200, skillProposal())
      .on('POST', '/api/v1/skill-proposals/sp-1/dismiss', 200, { id: 'sp-1', status: 'dismissed' })
    const { wrapper, toasts } = await mountView(SkillProposalView, fake, '/inbox/skills/sp-1')

    await wrapper.get('[data-test="dismiss"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/skill-proposals/sp-1/dismiss')).toBe(1)
    expect(toasts.items.value.map((toast) => toast.message)).toContain('Dismissed. Marshal will not propose this workflow again.')
    expect(wrapper.find('[data-test="download"]').exists()).toBe(false)
  })
})
