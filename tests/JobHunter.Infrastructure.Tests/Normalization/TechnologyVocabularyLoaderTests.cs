using JobHunter.Infrastructure.Normalization;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Normalization;

/// <summary>
/// T07: the committed technology vocabulary loads from the embedded YAML into a usable, unambiguous
/// vocabulary — the load that runs at host startup, so a malformed or ambiguous file fails here rather than
/// mis-tagging at run time. A handful of anchor technologies prove the file is wired and parsed, without the
/// test policing every one of the ~300 entries (that is the diff's job).
/// </summary>
public sealed class TechnologyVocabularyLoaderTests
{
    [Fact]
    public void The_embedded_vocabulary_loads_and_is_substantial()
    {
        var vocabulary = TechnologyVocabularyLoader.Load();

        vocabulary.Count.ShouldBeGreaterThan(200);
    }

    [Theory]
    [InlineData("We build services in Go and Rust.", "Go")]
    [InlineData("Strong C# and .NET background.", "C#")]
    [InlineData("Deploy to Kubernetes (k8s).", "Kubernetes")]
    [InlineData("Experience with golang required.", "Go")]
    [InlineData("Our stack is Node.js and PostgreSQL.", "PostgreSQL")]
    public void The_loaded_vocabulary_tags_anchor_technologies(string text, string expected)
    {
        TechnologyVocabularyLoader.Load().Match(text).ShouldContain(expected);
    }

    [Theory]
    // Agentic / LLM tooling (T10).
    [InlineData("We build on the Model Context Protocol.", "MCP")]
    [InlineData("Experience shipping with Claude and Anthropic.", "Claude")]
    [InlineData("You will call the OpenAI GPT models.", "OpenAI")]
    [InlineData("Familiarity with Gemini a plus.", "Gemini")]
    [InlineData("Pair-program inside Cursor daily.", "Cursor")]
    [InlineData("We serve models through Amazon Bedrock.", "Bedrock")]
    [InlineData("Deploy against Azure OpenAI in production.", "Azure OpenAI")]
    [InlineData("Inference runs on Vertex AI.", "Vertex AI")]
    [InlineData("Author agents with LangGraph.", "LangGraph")]
    [InlineData("We use Semantic Kernel for orchestration.", "Semantic Kernel")]
    [InlineData("Multi-agent systems with AutoGen.", "AutoGen")]
    [InlineData("Build crews in CrewAI.", "CrewAI")]
    [InlineData("Design agent orchestration for the platform.", "Agent Orchestration")]
    [InlineData("Robust tool calling and function calling.", "Function Calling")]
    [InlineData("Own prompt management across teams.", "Prompt Management")]
    [InlineData("Set up LLM eval harnesses.", "LLM Evaluation")]
    [InlineData("Add guardrails to the assistant.", "Guardrails")]
    [InlineData("Own fine-tuning pipelines.", "Fine-Tuning")]
    [InlineData("Stand up inference serving.", "Inference Serving")]
    [InlineData("Run models locally with Ollama.", "Ollama")]
    [InlineData("Ship on the Vercel AI SDK.", "Vercel AI SDK")]
    // Retrieval / vectors (T10).
    [InlineData("Retrieval-augmented generation over the docs.", "RAG")]
    [InlineData("We compute embeddings for search.", "Embeddings")]
    [InlineData("Store vectors in Pinecone.", "Vector Database")]
    [InlineData("Weaviate and Qdrant are in the stack.", "Vector Database")]
    [InlineData("We use pgvector on Postgres.", "Vector Database")]
    [InlineData("Front the models with an AI gateway.", "AI Gateway")]
    // Platform / infra (T10).
    [InlineData("Build our internal developer platform.", "Internal Developer Platform")]
    [InlineData("A platform engineering role.", "Platform Engineering")]
    [InlineData("Durable workflows on Temporal.", "Temporal")]
    [InlineData("Operate a service mesh across clusters.", "Service Mesh")]
    [InlineData("An event-driven architecture over queues.", "Event-Driven Architecture")]
    public void The_loaded_vocabulary_tags_target_stack_terms(string text, string expected)
    {
        TechnologyVocabularyLoader.Load().Match(text).ShouldContain(expected);
    }

    [Fact]
    public void The_target_stack_terms_do_not_over_tag_ordinary_prose()
    {
        // A description with none of the target-stack terms as whole tokens must tag none of them — the new
        // aliases are whole-token safe and must not fire on adjacent everyday words.
        var matches = TechnologyVocabularyLoader.Load().Match(
            "A collaborative team building reliable web services with a focus on clarity and craft.");

        matches.ShouldNotContain("MCP");
        matches.ShouldNotContain("RAG");
        matches.ShouldNotContain("Claude");
        matches.ShouldNotContain("Temporal");
        matches.ShouldNotContain("Vector Database");
        matches.ShouldNotContain("OpenAI");
    }

    [Fact]
    public void A_non_sequence_document_is_a_named_failure()
    {
        var ex = Should.Throw<TechnologyVocabularyException>(() =>
            TechnologyVocabularyLoader.Parse("canonical: Go"));

        ex.Message.ShouldContain("sequence");
    }

    [Fact]
    public void An_entry_with_no_canonical_name_is_a_named_failure()
    {
        var ex = Should.Throw<TechnologyVocabularyException>(() =>
            TechnologyVocabularyLoader.Parse("- aliases: [foo]"));

        ex.Message.ShouldContain("canonical");
    }

    [Fact]
    public void An_ambiguous_vocabulary_is_a_named_failure()
    {
        // "shared" is claimed by two canonical names — the vocabulary would tag inconsistently, so the load
        // is rejected with the offending spelling named.
        var yaml =
            """
            - canonical: Go
              aliases: [shared]
            - canonical: Rust
              aliases: [shared]
            """;

        var ex = Should.Throw<TechnologyVocabularyException>(() => TechnologyVocabularyLoader.Parse(yaml));

        ex.Message.ShouldContain("shared");
    }

    [Fact]
    public void An_empty_document_yields_an_empty_vocabulary()
    {
        TechnologyVocabularyLoader.Parse(string.Empty).Count.ShouldBe(0);
    }
}
