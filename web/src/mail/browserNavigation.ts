/** Full-page navigation away from the SPA (e.g. to an OAuth provider); an object so tests can replace it. */
export const browserNavigation = {
  assign(url: string) {
    window.location.assign(url)
  },
}
