import { useEffect, useState } from 'react'
import { api, type BuildVersion } from '../net/api'

/**
 * What build the server is running, in the corner of the screens you pass through anyway.
 *
 * **This is the only place the question is answerable without shell access.** The images carry
 * `org.opencontainers.image.revision` as an OCI label, which is correct and unreachable — a label
 * describes the image and is read with `docker inspect` on the host. TrueNAS cannot help either:
 * third-party app catalogues went away when Apps moved to Docker in 24.10, so a custom app has no
 * version listing and nowhere for a changelog to live.
 *
 * It reports the **server's** build, not this bundle's. The two images deploy separately, and a
 * constant compiled in here would describe whichever client you are holding rather than the server
 * it is talking to — which is the half that matters when you are asking whether a deploy landed.
 */
export function VersionBadge() {
  const [build, setBuild] = useState<BuildVersion | null>(null)

  useEffect(() => {
    let cancelled = false

    api
      .version()
      .then((v) => {
        if (!cancelled) setBuild(v)
      })
      // Deliberately silent. A version line is the least important thing on the screen, and a
      // server too unwell to answer this has already told the user in a way that matters more.
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [])

  if (!build) return null

  // A release says "v1.0.0". A build off a branch has no version to say, so it shows the commit
  // instead of claiming to be 0.0.0 - which would read like a real release of a very early one.
  const released = build.version !== '0.0.0' && build.version !== 'unknown'
  const label = released ? `v${build.version}` : build.shortRevision

  return (
    <p className="dim version-badge">
      <span title={build.revision}>{label}</span>
    </p>
  )
}
