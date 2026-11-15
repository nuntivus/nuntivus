# Nuntivus public website

Docusaurus presentation site, documentation and blog for **https://nuntivus.com**. This project is independent from the authenticated dashboard and needs no API connection, credentials or database.

## Develop

Use Node.js 24 and npm. From this directory:

```powershell
npm ci
npm start
```

Open http://localhost:5180. In Visual Studio, select the **Website** solution launch profile and press F5. Its background startup task runs `npm start -- --no-open` and waits for successful compilation before opening Chrome. Stop any manually started website server before debugging so port 5180 is available. Solution builds do not run the npm production build automatically, matching the dashboard convention.

## Validate and build

```powershell
npm test
npm run sync-docs
npm run typecheck
npm run build
npm run serve
```

`build/` contains the complete deployable static site. Upload the **contents** of this folder, not the source project or `node_modules`. `npm run serve` previews the production output locally on port 5180.

Start/build regenerate `docs/reference/` from the repository's `docs/` directory and copy the dashboard logo/favicon. Treat generated reference files as disposable: edit the original documentation instead. Website introduction, quick start and blog content are maintained here. Imported documents use Markdown mode, not executable MDX. Repository-only file links point to GitHub's main branch; reference content itself comes from the current checkout.

## Prepare AWS hosting (manual; nothing is provisioned)

1. Create a dedicated S3 bucket for website artifacts. Keep **Block Public Access enabled**. Use the S3 REST endpoint as the CloudFront origin, not S3 website hosting.
2. Create a CloudFront distribution with **Origin Access Control (OAC)**. Its S3 bucket policy should grant `s3:GetObject` to the CloudFront service principal only for this distribution's source ARN.
3. Set the default root object to `index.html`, redirect HTTP to HTTPS and enable compression. Use a response headers policy for `X-Content-Type-Options: nosniff`, an appropriate referrer policy and HSTS after HTTPS is verified. Font loading currently uses Google Fonts; allow it if adding a Content Security Policy.
4. Request/validate an ACM certificate in **us-east-1** covering `nuntivus.com` (and `www.nuntivus.com` only if desired). Add those alternate domain names to CloudFront and configure Route 53 alias records or equivalent DNS records.
5. Publish `hosting/cloudfront-function.js` as a CloudFront Function and associate it with the **viewer-request** event of the website behavior. It rewrites clean documentation/blog URLs to their generated `index.html` objects while leaving assets unchanged.
6. Configure CloudFront 403 and 404 custom error responses to serve `/404.html` with HTTP status **404** and a short error cache lifetime. Do not use an SPA fallback to `/index.html`: missing pages should remain 404s.
7. Upload `build/` contents to the bucket using your approved AWS credentials/workflow. No credentials belong in this repository. Only delete stale objects if the bucket/prefix is dedicated to this site.
8. Give fingerprinted `/assets/*` files a long immutable cache lifetime. Use a short cache lifetime for HTML, sitemap, RSS, search indexes and other unversioned files. Invalidate changed HTML/unversioned paths after deployment; do not overwrite immutable assets with different content under the same name.
9. Verify `/`, `/docs/intro/`, `/docs/quick-start/`, a nested reference page, `/blog/`, search, assets and an unknown URL. Check HTTPS and canonical URLs. Keep the previous build artifacts for rollback.

No AWS resources are created and no files are uploaded by this project. The CloudFront function is supplied for review and manual association; it is not deployed automatically.

## Content and branding

- Homepage: `src/pages/index.tsx`.
- Theme: `src/css/custom.css`; dashboard SVG assets are reused under the repository license.
- Canonical URL/navigation/SEO/search: `docusaurus.config.ts`.
- Documentation navigation: `sidebars.ts`.
- Blog: dated Markdown files in `blog/`, authors in `blog/authors.yml`.

Keep early-development notices and at-least-once semantics visible. Do not add production-readiness claims, deferred SDK/CLI features or customer data to marketing material. Blog dates and authors should reflect the actual publication when released. The SVG social image is suitable for the site, but some social crawlers require PNG/JPEG: export a 1200×630 raster version before promoting public social previews.

## Dependency review

The lockfile includes patched `serialize-javascript` and `tinypool` overrides, verified with a production build. The current npm audit still reports low/moderate transitive advisories involving `deepmerge` and the development server's `uuid` dependency. These packages are not uploaded to S3: deploy only `build/`. Do not expose the development server publicly or build untrusted content with privileged credentials. Recheck advisories and upstream fixes before public release; do not apply `npm audit fix --force` blindly, because its proposed downgrade changes Docusaurus compatibility.

For optional browser smoke checks, install the dashboard dependencies/browser tooling, serve the website build, then run `node scripts/smoke-test.mjs`. Set `WEBSITE_TEST_URL` to override the default localhost URL. This reuses dashboard Playwright only for development checks, not production.
