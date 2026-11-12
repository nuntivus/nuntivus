type ClerkSession = { getToken: (options: { template: string }) => Promise<string | null> }
type ClerkClient = {
  session: ClerkSession | null | undefined
  mountSignIn: (host: HTMLDivElement) => void
  unmountSignIn: (host: HTMLDivElement) => void
}

export function createClerkPortalSession(client: ClerkClient, host: HTMLDivElement | null, localToken: () => string, update: (token: string, signedIn: boolean) => void) {
  let disposed = false
  let mounted = false
  let revision = 0
  function showSignIn(show: boolean) {
    if (!host || mounted === show) return
    if (show) client.mountSignIn(host)
    else client.unmountSignIn(host)
    mounted = show
  }
  return {
    async refresh() {
      const current = ++revision
      if (localToken()) {
        showSignIn(false)
        update(localToken(), false)
        return
      }
      const session = client.session
      const token = await session?.getToken({ template: 'nuntivus-portal' }) ?? ''
      if (disposed || current !== revision || localToken() || client.session !== session) return
      showSignIn(!session)
      update(token, !!session)
    },
    dispose() {
      disposed = true
      revision++
      showSignIn(false)
    },
  }
}
