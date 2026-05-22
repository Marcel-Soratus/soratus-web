using Soratus.Web.Models;

namespace Soratus.Web.Services;

public static class JsonLd
{
    public static string Build(CompanyOptions co)
    {
        var url = co.Url;
        return $$"""
        {
          "@context": "https://schema.org",
          "@graph": [
            {
              "@type": "Organization",
              "@id": "{{url}}#organization",
              "name": "{{co.LegalName}}",
              "url": "{{url}}",
              "logo": "{{url}}/brand/og-image.png?v=2",
              "image": "{{url}}/brand/og-image.png?v=2",
              "email": "{{co.Email}}",
              "address": { "@type": "PostalAddress", "addressCountry": "NL" },
              "taxID": "{{co.Kvk}}",
              "sameAs": []
            },
            {
              "@type": "WebSite",
              "@id": "{{url}}#website",
              "url": "{{url}}",
              "name": "Soratus",
              "publisher": { "@id": "{{url}}#organization" },
              "inLanguage": "nl-NL"
            },
            {
              "@type": "ProfessionalService",
              "@id": "{{url}}#service",
              "name": "Soratus AI-development",
              "provider": { "@id": "{{url}}#organization" },
              "areaServed": "NL",
              "serviceType": "AI agents, custom software, integraties",
              "description": "AI agents, custom software in .NET 10 / Blazor / Azure, integraties met Exact, AFAS, Salesforce, Shopify, HubSpot, Slack, Teams, WhatsApp, Outlook."
            },
            {
              "@type": "FAQPage",
              "mainEntity": [
                {
                  "@type": "Question",
                  "name": "Wat doet Soratus?",
                  "acceptedAnswer": { "@type": "Answer", "text": "Soratus bouwt AI-agents, custom software en integraties voor MKB en enterprise. Gevestigd in Nederland, EU-hosted, AVG-proof." }
                },
                {
                  "@type": "Question",
                  "name": "Hoe snel kan iets live staan?",
                  "acceptedAnswer": { "@type": "Answer", "text": "Een eenvoudige AI-agent staat binnen 14 dagen live. Complexere systemen tussen 4 en 8 weken. Vaste prijzen, geen uurtarief." }
                },
                {
                  "@type": "Question",
                  "name": "Welke integraties ondersteunen jullie?",
                  "acceptedAnswer": { "@type": "Answer", "text": "Exact, AFAS, Salesforce, Shopify, HubSpot, Slack, Microsoft Teams, WhatsApp en Outlook. Custom integraties op aanvraag." }
                },
                {
                  "@type": "Question",
                  "name": "Hoe zit het met privacy en data?",
                  "acceptedAnswer": { "@type": "Answer", "text": "EU-hosted, AVG-proof, ISO 27001 via partners. Wij trainen niet op klantdata en bieden volledige audit-trail." }
                }
              ]
            }
          ]
        }
        """;
    }
}
