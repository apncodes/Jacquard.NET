import { themes as prismThemes } from 'prism-react-renderer';
import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Strands Agents .NET',
  tagline: 'Model-driven agentic AI for C# developers',
  favicon: 'img/favicon.ico',

  url: 'https://apncodes.github.io',
  baseUrl: '/StrandsAgents.net/',

  organizationName: 'apncodes',
  projectName: 'StrandsAgents.net',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  onBrokenMarkdownLinks: 'warn',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/apncodes/StrandsAgents.net/tree/main/docs/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/social-card.png',
    navbar: {
      title: 'Strands Agents .NET',
      logo: {
        alt: 'Strands Agents .NET',
        src: 'img/logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          href: 'https://github.com/apncodes/StrandsAgents.net',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://www.nuget.org/packages/StrandsAgents.Core',
          label: 'NuGet',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            { label: 'Getting Started', to: '/docs/intro' },
            { label: 'Concepts', to: '/docs/concepts/agent-event-loop' },
            { label: 'Tutorials', to: '/docs/tutorials/first-agent' },
          ],
        },
        {
          title: 'Community',
          items: [
            {
              label: 'GitHub Discussions',
              href: 'https://github.com/apncodes/StrandsAgents.net/discussions',
            },
            {
              label: 'Issues',
              href: 'https://github.com/apncodes/StrandsAgents.net/issues',
            },
          ],
        },
        {
          title: 'More',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/apncodes/StrandsAgents.net',
            },
            {
              label: 'NuGet',
              href: 'https://www.nuget.org/packages/StrandsAgents.Core',
            },
            {
              label: 'Strands Agents',
              href: 'https://strandsagents.com',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Strands Agents .NET Contributors. Apache 2.0 License.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json', 'yaml'],
    },
    colorMode: {
      defaultMode: 'light',
      disableSwitch: false,
      respectPrefersColorScheme: true,
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
