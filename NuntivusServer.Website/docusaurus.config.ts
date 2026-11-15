import type { Config } from '@docusaurus/types'
import type { Options, ThemeConfig } from '@docusaurus/preset-classic'
import { themes } from 'prism-react-renderer'

const config: Config = {
  title: 'Nuntivus',
  tagline: 'Your events. Every customer. Their destination.',
  url: 'https://www.nuntivus.com',
  baseUrl: '/',
  trailingSlash: true,
  favicon: 'img/favicon.svg',
  organizationName: 'nuntivus',
  projectName: 'nuntivus-server',
  onBrokenLinks: 'throw',
  markdown: { format: 'detect', hooks: { onBrokenMarkdownLinks: 'throw' } },
  i18n: { defaultLocale: 'en', locales: ['en'] },
  presets: [['classic', {
    sitemap: {
      filename: 'sitemap.xml',
      changefreq: null,
      priority: null,
      ignorePatterns: ['/search/**', '/404.html'],
    },
    docs: { sidebarPath: './sidebars.ts', showLastUpdateTime: false },
    blog: { showReadingTime: true, blogTitle: 'Nuntivus blog', blogDescription: 'Project news, architecture and self-hosted event delivery.', onInlineAuthors: 'throw', onUntruncatedBlogPosts: 'throw' },
    theme: { customCss: './src/css/custom.css' },
  } satisfies Options]],
  themes: [['@easyops-cn/docusaurus-search-local', { hashed: true, indexDocs: true, indexBlog: true, indexPages: true, docsRouteBasePath: '/docs', highlightSearchTermsOnTargetPage: true }]],
  themeConfig: {
    image: 'img/social-card.svg',
    colorMode: { defaultMode: 'dark', respectPrefersColorScheme: true },
    announcementBar: {
      id: 'alpha-status',
      content: '<strong>α Alpha software — not ready for production.</strong> For evaluation and testing only. APIs and behavior may change.',
      backgroundColor: 'var(--alpha-background)',
      textColor: 'var(--alpha-text)',
      isCloseable: false,
    },
    navbar: {
      title: 'Nuntivus α', logo: { alt: 'Nuntivus logo', src: 'img/logo.svg' },
      items: [
        { to: '/', label: 'Product', position: 'left' },
        { type: 'docSidebar', sidebarId: 'docs', label: 'Documentation', position: 'left' },
        { to: '/blog', label: 'Blog', position: 'left' },
        { href: 'https://github.com/nuntivus/nuntivus-server', label: 'GitHub', position: 'right' },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        { title: 'Build', items: [{ label: 'Get started', to: '/docs/quick-start' }, { label: 'Architecture', to: '/docs/reference/architecture' }] },
        { title: 'Explore', items: [{ label: 'Documentation', to: '/docs/intro' }, { label: 'Blog', to: '/blog' }] },
        { title: 'Open source', items: [{ label: 'GitHub', href: 'https://github.com/nuntivus/nuntivus-server' }, { label: 'Report an issue', href: 'https://github.com/nuntivus/nuntivus-server/issues' }, { label: 'Apache-2.0 license', href: 'https://github.com/nuntivus/nuntivus-server/blob/main/LICENSE' }] },
      ],
      copyright: `© ${new Date().getFullYear()} Nuntivus contributors. Built in the open.`,
    },
    prism: { theme: themes.github, darkTheme: themes.dracula, additionalLanguages: ['powershell', 'csharp', 'json', 'bash', 'yaml'] },
  } satisfies ThemeConfig,
}
export default config
