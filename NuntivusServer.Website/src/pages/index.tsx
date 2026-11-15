import type { ReactNode } from 'react'
import Layout from '@theme/Layout'
import Link from '@docusaurus/Link'

const features = [
  ['01', 'One event. Many destinations.', 'Define customer contracts once and deliver to HTTP, Kafka, RabbitMQ, NATS or SQS without embedding routing in your application.'],
  ['02', 'Shape every delivery.', 'Validate schemas, review compatibility, filter events and transform payloads. Simulate the full contract before sending.'],
  ['03', 'See what happened.', 'Inspect delivery history, retry failures, replay events and monitor success and latency targets with SLOs and alerts.'],
  ['04', 'Keep control of your infrastructure.', 'Run the API, Worker and dashboard yourself. Keep your data, credentials and delivery policies in your own environment.'],
]

export default function Home(): ReactNode {
  return <Layout title="Self-hosted customer event infrastructure" description="Publish business events once. Deliver to every customer's destination with Nuntivus: open-source routing, schema validation, retries and delivery visibility.">
    <main>
      <section className="site-hero site-container">
        <div className="hero-copy">
          <span className="site-eyebrow">OPEN SOURCE · SELF HOSTED · APACHE 2.0</span>
          <div className="alpha-notice" role="note" aria-label="Alpha release status"><strong className="alpha-badge"><span aria-hidden="true">α</span> Alpha</strong><span>Not ready for production. Use for evaluation and testing only.</span></div>
          <h1>Your events.<br />Every customer.<br /><span>Their destination.</span></h1>
          <p>Publish business events once. Let Nuntivus handle customer-specific routing, payloads and delivery—on infrastructure you control.</p>
          <div className="site-actions"><Link className="button button--primary button--lg" to="/docs/quick-start">Get started →</Link><Link className="button button--outline button--secondary button--lg" href="https://github.com/nuntivus/nuntivus-server">Explore on GitHub</Link></div>
          <p className="hero-note">Alpha release: APIs, configuration and behavior may change before a stable release.</p>
        </div>
        <div className="event-diagram" aria-label="Sources send events to Nuntivus, where customer contracts route deliveries to HTTP, Kafka, RabbitMQ, NATS and SQS">
          <div className="diagram-toolbar"><span /><span /><span /><code>event delivery, your way</code></div>
          <div className="diagram-input"><span className="diagram-tag">YOUR APPLICATION</span><code>orders.created</code><span className="diagram-arrow" aria-hidden="true">↓</span></div>
          <div className="diagram-core"><img src="/img/logo.svg" alt="" width="42" height="42" /><div><strong>Nuntivus</strong><small>Validate · Route · Transform · Deliver</small></div></div>
          <div className="diagram-contracts"><span>Customer A</span><span>Customer B</span><span>Customer C</span></div>
          <div className="diagram-outputs">{['HTTP', 'Kafka', 'RabbitMQ', 'NATS', 'SQS'].map(type => <span key={type}>{type}</span>)}</div>
          <div className="diagram-footnote"><span className="status-dot" /> Delivery history · Retries · SLOs · Alerts</div>
        </div>
      </section>
      <section className="integration-strip"><div className="site-container"><span>Built for different delivery needs</span><div>{['HTTP', 'Apache Kafka', 'RabbitMQ', 'NATS', 'Amazon SQS'].map(name => <strong key={name}>{name}</strong>)}</div></div></section>
      <section className="site-container site-section" aria-labelledby="architecture-heading">
        <span className="site-eyebrow">A CLEAR PATH FROM EVENT TO DELIVERY</span>
        <h2 id="architecture-heading">Durable at the core.<br />Independent at every boundary.</h2>
        <p className="section-intro">The API accepts events. PostgreSQL keeps the durable record. JetStream distributes work to the Worker, where customer contracts shape every delivery.</p>
        <figure className="architecture-figure">
          <div className="architecture-viewport" tabIndex={0} role="region" aria-label="Runtime architecture diagram; scroll horizontally on smaller screens">
            <img src="/img/docs/assets/architecture.svg" alt="Nuntivus runtime architecture: HTTP sources through the API and pull connectors in the Worker persist events and outbox records to PostgreSQL. The outbox publisher sends work through NATS JetStream to contract matching and delivery workers, which send to customer destinations and persist delivery results." width="1200" height="900" loading="lazy" />
          </div>
          <figcaption><span>At-least-once processing. PostgreSQL is the source of truth.</span><a href="/img/docs/assets/architecture.svg" target="_blank" rel="noopener noreferrer">Open full diagram ↗</a></figcaption>
        </figure>
        <Link to="/docs/reference/architecture">Explore the architecture →</Link>
      </section>
      <section id="platform" className="site-container site-section"><span className="site-eyebrow">LESS DELIVERY GLUE. MORE PRODUCT.</span><h2>The space between your events<br />and your customers, handled.</h2><p className="section-intro">A dedicated delivery layer, not another set of integration branches in your application.</p><div className="feature-grid">{features.map(([number, title, description]) => <article className="feature-card" key={number}><span className="feature-number">{number}</span><h3>{title}</h3><p>{description}</p></article>)}</div></section>
      <section className="site-container site-section"><div className="getting-started"><div><span className="site-eyebrow">FROM CHECKOUT TO FIRST LOOK</span><h2>Try it on your machine.</h2><p>Build the API, Worker and administration dashboard from source with Docker Compose.</p><Link to="/docs/quick-start">Read the quick start →</Link><p className="hero-note">Local evaluation only. Production hosting has separate security requirements.</p></div><div className="terminal-card"><div className="terminal-title">TERMINAL</div><pre><code>{'git clone https://github.com/nuntivus/nuntivus-server.git\ncd nuntivus-server\ndocker compose -f docker-compose.quickstart.yml up -d --wait'}</code></pre></div></div></section>
      <section className="site-container site-section final-cta"><h2>Built in the open.<br />Ready for your feedback.</h2><p>Explore the architecture, try the project and help shape what comes next.</p><div className="site-actions"><Link className="button button--primary button--lg" to="/docs/intro">Explore the documentation</Link><Link className="button button--outline button--secondary button--lg" to="/blog">Read the blog</Link></div><p className="hero-note">At-least-once processing: consumers must tolerate duplicate deliveries.</p></section>
    </main>
  </Layout>
}
