import React from 'react';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';

const agentSnippet = `using Jacquard.Core;
using Jacquard.Models.Bedrock;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]
);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);`;

export default function Home(): React.JSX.Element {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description={siteConfig.tagline}>
      <main style={{ padding: '4rem 2rem', textAlign: 'center' }}>
        <h1>{siteConfig.title}</h1>
        <p style={{ fontSize: '1.25rem', marginBottom: '2rem' }}>{siteConfig.tagline}</p>
        <Link
          className="button button--primary button--lg"
          to="/docs/intro">
          Get Started
        </Link>

        <div style={{ maxWidth: '640px', margin: '3rem auto 0', textAlign: 'left' }}>
          <h3 style={{ textAlign: 'center', marginBottom: '1rem' }}>Build an agent in 10 lines</h3>
          <CodeBlock language="csharp">{agentSnippet}</CodeBlock>
        </div>
      </main>
    </Layout>
  );
}
