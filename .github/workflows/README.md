# Workflows

## Removed: `api-build-deploy.yml` (2026-09-06)

Built the API image in Actions, pushed it to DigitalOcean Container Registry and
asked DO App Platform to pull-and-roll. It fired on every push to `main` under
`src/**`.

**Why it went.** DigitalOcean is no longer used — everything is self-hosted on
the VM. The workflow had also been failing since **2026-08-01**, in under fifteen
seconds each time, at its first step:

    doctl auth init
    Validating token... ✘
    Error: 401 Unable to authenticate you

`DIGITALOCEAN_ACCESS_TOKEN` expired, and the last successful deploy was
**2026-07-05**. So it was noise on every commit rather than a deploy path, and
leaving it would have kept implying `main` deploys somewhere. It does not.

It is recoverable from history if a hosted deploy ever returns:

    git show 848319e:.github/workflows/api-build-deploy.yml

Worth keeping from it if so: the image was pre-built in Actions rather than by
the platform, because DO's kaniko builders spent 5–7 minutes compiling .NET and
installing TeX Live on *every* deploy. Caching the layers in Actions took
end-to-end deploys from ~10 minutes to ~4–5.

## Kept: `ef-migrations-check.yml`

Not on the deploy path; it guards migrations and still earns its run.
