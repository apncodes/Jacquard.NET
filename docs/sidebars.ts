import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    {
      type: 'doc',
      id: 'intro',
      label: 'Introduction',
    },
    {
      type: 'doc',
      id: 'getting-started',
      label: 'Getting Started',
    },
    {
      type: 'category',
      label: 'Concepts',
      collapsed: false,
      items: [
        'concepts/agent-event-loop',
        'concepts/tools',
        'concepts/itoolprovider',
        'concepts/hooks',
        'concepts/sessions',
        'concepts/multi-agent',
      ],
    },
    {
      type: 'category',
      label: 'Tutorials',
      items: [
        'tutorials/first-agent',
        'tutorials/di-production',
        'tutorials/aot-lambda',
      ],
    },
    {
      type: 'category',
      label: 'How-To',
      items: [
        'how-to/add-a-tool',
        'how-to/deploy-to-lambda',
        'how-to/durable-workflows',
      ],
    },
    {
      type: 'category',
      label: 'Migration',
      items: [
        'migration/from-maf',
      ],
    },
    {
      type: 'doc',
      id: 'faq',
      label: 'FAQ & Troubleshooting',
    },
  ],
};

export default sidebars;
