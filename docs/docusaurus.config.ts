import { themes as prismThemes } from 'prism-react-renderer';
import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Jacquard.NET',
  tagline: 'Native C# SDK for building agentic AI',
  favicon: 'img/favicon.ico',

  url: 'https://apncodes.github.io',
  baseUrl: '/Jacquard.NET/',

  organizationName: 'apncodes',
  projectName: 'Jacquard.NET',
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
          editUrl: 'https://github.com/apncodes/Jacquard.NET/tree/main/docs/',
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
      title: 'Jacquard.NET',
      logo: {
        alt: 'Jacquard.NET',
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
          href: 'https://github.com/apncodes/Jacquard.NET',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://www.nuget.org/packages/Jacquard.Core',
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
              href: 'https://github.com/apncodes/Jacquard.NET/discussions',
            },
            {
              label: 'Issues',
              href: 'https://github.com/apncodes/Jacquard.NET/issues',
            },
          ],
        },
        {
          title: 'More',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/apncodes/Jacquard.NET',
            },
            {
              label: 'NuGet',
              href: 'https://www.nuget.org/packages/Jacquard.Core',
            },
            {
              label: 'Strands Agents',
              href: 'https://strandsagents.com',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Jacquard.NET Contributors. Apache 2.0 License.`,
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
