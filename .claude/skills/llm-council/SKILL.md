---
name: llm-council
description: "Run a TicketMaster engineering question, idea, or decision through a council of 5 AI advisors who independently analyze it, peer-review each other anonymously, and synthesize a final verdict. Based on Karpathy's LLM Council methodology. MANDATORY TRIGGERS: 'council this', 'run the council', 'war room this', 'pressure-test this', 'stress-test this', 'debate this'. STRONG TRIGGERS (use when combined with a real decision or tradeoff): 'should we X or Y', 'which option', 'what would you do', 'is this the right move', 'validate this', 'get multiple perspectives', 'I can't decide', 'I'm torn between' — e.g. RPC vs integration event, a service boundary, a consistency model, a data store, reversing an ADR, the order of known-gaps work. Do NOT trigger on simple yes/no questions, factual lookups, code review (use code-review), bugs (use systematic-debugging), or choices an ADR or skill already settles. DO trigger when the user presents a genuine decision with stakes, multiple options, and context that suggests they want it pressure-tested from multiple angles."
---

# LLM Council

You ask one AI a question, you get one answer. That answer might be great. It might be mid. You have no way to tell because you only saw one perspective.

The council fixes this. It runs your question through 5 independent advisors, each thinking from a fundamentally different angle. Then they review each other's work. Then a chairman synthesizes everything into a final recommendation that tells you where the advisors agree, where they clash, and what you should actually do.

This is adapted from Andrej Karpathy's LLM Council. He dispatches queries to multiple models, has them peer-review each other anonymously, then a chairman produces the final answer. We do the same thing inside Claude using sub-agents with different thinking lenses instead of different models.

---

## when to run the council

The council is for questions where being wrong is expensive. In this codebase that means a cross-service contract, a consistency or failure model, where state lives, the architecture of a service, reversing an ADR, or which known gap to close next and how.

Good council questions:
- "Should the gateway validate JWTs itself instead of calling Users.Api on every request?"
- "Should Bookings keep calling Events over gRPC for seat validation, or trust what `EventCreated` carries?"
- "Partial refunds hold seats until `BookingRefunded` — is that the right consistency model?"
- "Which known gap should we close next, and in what shape?"
- "Here's my design for customer notifications. What's weak?"

Bad council questions:
- "What does `TransactionBehavior` do?" (one right answer, read the code)
- "Write the migration for X" (creation task, not a decision)
- "Is this handler correct?" (a review, use `code-review`)

The council shines when there's genuine uncertainty and the cost of a bad call is high. If you already know the answer and just want validation, the council will likely tell you things you don't want to hear. That's the point.

Not a council question: anything an ADR in `.claude/knowledge/decisions/`, a service skill, or `common-antipatterns.md` already answers — answer from that file and cite it. Decisions already closed stay closed: the booking flow stays choreographed through integration events with no saga or process manager, so do not council a saga unless the user asks for it by name.

A run costs 11 sub-agents (5 advisors, 5 reviewers, 1 chairman). If the question looks small, say so in one line, then run it anyway when it was explicitly triggered.

---

## the five advisors

Each advisor thinks from a different angle. They're not job titles or personas. They're thinking styles that naturally create tension with each other.

### 1. The Contrarian

Actively looks for what's wrong, what's missing, what will fail. Assumes the idea has a fatal flaw and tries to find it. If everything looks solid, digs deeper. The Contrarian is not a pessimist. They're the friend who saves you from a bad deal by asking the questions you're avoiding.

In this system the fatal flaw is usually one of: a race across instances, a message lost or handled twice, a transaction that commits on one side only, a status the state machine can't leave, money that moves without a ledger entry, a contract change that breaks a consumer mid-deploy.

### 2. The First Principles Thinker

Ignores the surface-level question and asks "what are we actually trying to solve here?" Strips away assumptions. Rebuilds the problem from the ground up. Sometimes the most valuable council output is the First Principles Thinker saying "you're asking the wrong question entirely."

Here that means naming the invariant the proposal must protect — a seat sold once, a refund never exceeding what was paid, a service starting without another — and rebuilding from it.

### 3. The Expansionist

Looks for upside everyone else is missing. What could be bigger? What adjacent opportunity is hiding? What's being undervalued? The Expansionist doesn't care about risk (that's the Contrarian's job). They care about what happens if this works even better than expected — which other service could reuse it, which known gap it closes in passing, what gets simpler later.

### 4. The Outsider

Has zero context about you, your field, or your history. Responds purely to what's in front of them. This is the most underrated advisor. Experts develop blind spots. The Outsider catches the curse of knowledge: things that are obvious to you but confusing to everyone else.

Here the Outsider is a competent engineer who has never seen this repo. They get **only the framed question** and no file access, so they catch jargon, unstated assumptions, and a design only its author could follow or operate.

### 5. The Executor

Only cares about one thing: can this actually be done, and what's the fastest path to doing it? Ignores theory, strategy, and big-picture thinking. The Executor looks at every idea through the lens of "OK but what do you do Monday morning?" If an idea sounds brilliant but has no clear first step, the Executor will say so.

Here that means: which projects change, whether a migration is needed, which `Tests/<Service>/*` projects prove it, what has to deploy first, what can ship alone.

**Why these five:** They create three natural tensions. Contrarian vs Expansionist (downside vs upside). First Principles vs Executor (rethink everything vs just do it). The Outsider sits in the middle keeping everyone honest by seeing what fresh eyes see.

---

## how a council session works

### step 1: frame the question (with context enrichment)

When the user says "council this" (or any trigger phrase), do two things before framing:

**A. Scan the workspace for context.** The user's question is often just the tip of the iceberg. Before framing, read the files that would make the advisors' answers specific instead of generic:
- `CLAUDE.md` — architecture, cross-cutting patterns, conventions
- `.claude/knowledge/README.md`, then the ADRs and `common-antipatterns.md` sections the question touches. Note every deliberate exception the proposal would undo
- The service skill(s) involved (`bookings-service`, `payments-service`, `events-service`, `users-service`, `api-gateway`) and the technology skill (`messaging`, `rpc`, `efcore`, `document-db`, `cqrs`, `testing`), especially their known-gaps sections
- Your project memory, if you have one — it may hold a known-gaps to-do list and the user's preferences. Everything this skill depends on is written here, so it works without one
- Any files the user explicitly referenced
- The actual code at the seam in question: a quick grep for the handler, contract or consumer, so the framing names real types and files instead of paraphrasing them

Don't spend more than a minute on this. You're looking for the handful of files that ground the advice.

**B. Frame the question.** Take the user's raw question AND the enriched context and reframe it as a clear, neutral prompt that all five advisors will receive. The framed question should include:
1. The core decision or question, and the options on the table
2. Key context from the user's message
3. The current behaviour, with file paths and type names
4. Constraints: relevant ADRs and deliberate exceptions, service ownership, the user's preferences (no static helper classes, no folder reorganisation, comment only the non-obvious), test layout
5. What's at stake — which invariant, which failure, what is hard to reverse

Don't add your own opinion. Don't steer it. But DO make sure each advisor has enough context to give a specific, grounded answer rather than generic advice.

If the question is too vague, don't ask — this workspace prefers a call over a question. Frame your best reading, state the assumption in one line, and proceed.

Save the framed question; the chairman needs it.

### step 2: convene the council (5 sub-agents in parallel)

Spawn all 5 advisors simultaneously: five `Agent` calls in **one message**, `general-purpose` type. Each gets:
1. Their advisor identity and thinking style (from the descriptions above)
2. The framed question
3. A clear instruction: respond independently. Do not hedge. Do not try to be balanced. Lean fully into your assigned perspective. If you see a fatal flaw, say it. If you see massive upside, say it. Your job is to represent your angle as strongly as possible. The synthesis comes later.

The Contrarian, First Principles Thinker, Expansionist and Executor may read the files the framing names to check a claim, but must not modify anything. The Outsider must not open any file.

Each advisor should produce a response of 150-300 words. Long enough to be substantive, short enough to be scannable.

**Sub-agent prompt template:**

```
You are [Advisor Name] on an LLM Council reviewing an engineering decision in TicketMaster, a .NET 10
microservice system (Users, Events, Bookings, Payments behind a YARP gateway; RabbitMQ via Wolverine
with outboxes; Postgres, Cosmos DB, Redis).

Your thinking style: [advisor description from above]

[Outsider only: You have never seen this codebase. Do not read any file. Respond only to the text below.]
[Everyone else: You may read the files named below to verify a claim. Do not modify anything.]

A user has brought this question to the council:

---
[framed question]
---

Respond from your perspective. Be direct and specific. Don't hedge or try to be balanced. Lean fully into your assigned angle. The other advisors will cover the angles you're not covering.

Name the type, file, message or failure sequence you mean. If something is a guess rather than something you read, say so.

Keep your response between 150-300 words. No preamble. Go straight into your analysis.
```

### step 3: peer review (5 sub-agents in parallel)

This is the step that makes the council more than just "ask 5 times." It's the core of Karpathy's insight.

Collect all 5 advisor responses. Anonymize them as Response A through E (randomize which advisor maps to which letter so there's no positional bias), and keep the mapping.

Spawn 5 new sub-agents in one message, one for each advisor. They need no file access. Each reviewer sees all 5 anonymized responses and answers three questions:
1. Which response is the strongest and why? (pick one)
2. Which response has the biggest blind spot and what is it?
3. What did ALL responses miss that the council should consider?

**Reviewer prompt template:**

```
You are reviewing the outputs of an LLM Council. Five advisors independently answered this engineering question about TicketMaster:

---
[framed question]
---

Here are their anonymized responses:

**Response A:**
[response]

**Response B:**
[response]

**Response C:**
[response]

**Response D:**
[response]

**Response E:**
[response]

Answer these three questions. Be specific. Reference responses by letter.
1. Which response is the strongest? Why?
2. Which response has the biggest blind spot? What is it missing?
3. What did ALL five responses miss that the council should consider — a failure mode, a consumer, an operational cost, a test that would prove or disprove the proposal?

Keep your review under 200 words. Be direct.
```

### step 4: chairman synthesis

This is the final step. One agent gets everything: the original question, all 5 advisor responses (now de-anonymized so you can see which advisor said what), and all 5 peer reviews.

The chairman's job is to produce the final council output. It follows this structure:

**COUNCIL VERDICT**
1. **Where the council agrees** — the points that multiple advisors converged on independently. These are high-confidence signals.
2. **Where the council clashes** — the genuine disagreements. Don't smooth these over. Present both sides and explain why reasonable advisors disagree.
3. **Blind spots the council caught** — things that only emerged through the peer review round. Things individual advisors missed that other advisors flagged.
4. **The recommendation** — a clear, actionable recommendation. Not "it depends." Not "consider both sides." A real answer. The chairman can disagree with the majority if the reasoning supports it. It names any ADR or deliberate exception it reverses and why that is justified.
5. **The one thing you should do first** — a single concrete next step: a commit, a test, a spike, or a question to settle. Not a list of 10 things. One thing.

**Chairman prompt template:**

```
You are the Chairman of an LLM Council. Your job is to synthesize the work of 5 advisors and their peer reviews into a final verdict on a TicketMaster engineering decision.

The question brought to the council:

---
[framed question]
---

ADVISOR RESPONSES:

**The Contrarian:**
[response]

**The First Principles Thinker:**
[response]

**The Expansionist:**
[response]

**The Outsider:**
[response]

**The Executor:**
[response]

PEER REVIEWS:
[all 5 peer reviews]

Produce the council verdict using this exact structure:

## Where the Council Agrees
[Points multiple advisors converged on independently. These are high-confidence signals.]

## Where the Council Clashes
[Genuine disagreements. Present both sides. Explain why reasonable advisors disagree.]

## Blind Spots the Council Caught
[Things that only emerged through peer review. Things individual advisors missed that others flagged.]

## The Recommendation
[A clear, direct recommendation. Not "it depends." A real answer with reasoning. Name any ADR or deliberate exception it reverses and why that is justified.]

## The One Thing to Do First
[A single concrete next step. Not a list. One thing.]

Be direct. Don't hedge. The whole point of the council is to give the user clarity they couldn't get from a single perspective.
```

### step 5: present the verdict in chat

After the chairman synthesis is complete, present the full verdict directly in chat using markdown. Do NOT generate an HTML report or any files. The user reads it in the conversation.

Format the output as:

```
## Council Verdict: {short topic}

### Where the Council Agrees
{content}

### Where the Council Clashes
{content}

### Blind Spots the Council Caught
{content}

### The Recommendation
{content}

### The One Thing to Do First
{content}
```

Keep it scannable. Use bullet points. Reference code as `path:line` where the advisors pointed at it. Before presenting, verify any claim about the code that the recommendation depends on — advisors guess.

### step 6: record the outcome, not the debate

Don't save a transcript; the council leaves no file behind. When the user accepts the recommendation and it is implemented, fold the decision into the same change, per the repo's convention:
- A cross-service choice → a new ADR from `.claude/knowledge/decisions/template.md` with the next number, plus its line in `.claude/knowledge/README.md`. Reversing an ADR marks the old one `Superseded by ADR-NNN`.
- A choice inside one service or technology → the relevant skill and, where it changes the overview, `CLAUDE.md`.
- A gap it closes or opens → the skill's known-gaps section, and any known-gaps list in your project memory.

Never commit as part of a council run; commits happen only when the user asks.

---

## example: counciling a gateway decision

Illustrative — what each lens would be expected to raise, not a recorded run.

**User:** "Council this: the gateway calls Users.Api `api/users/auth` on every request through `UsersServiceAuthHandler`. Should it validate the JWT signature itself instead?"

**The Contrarian:** "Local validation can't see revocation: a token issued before a role change stays valid until it expires. Key rotation now has to reach the gateway. And the claims the gateway materialises (`FirstName`, `LastName`) come from the Users response — `TokenService` doesn't put them in the token..."

**The First Principles Thinker:** "The invariant is 'a request downstream carries an identity Users vouches for.' Is the problem latency, availability of Users, or both? Each points to a different fix..."

**The Expansionist:** "This removes the hottest service-to-service hop in the system. The gateway keeps serving authenticated reads while Users.Api is down, and it opens the door to scopes and per-route claims..."

**The Outsider:** "'Introspection', '`X-Identity-*` headers', 'edge auth' — which component is actually trusted to decide who the caller is, and is that written down anywhere?"

**The Executor:** "First commit: validate signature and expiry in the gateway behind the existing scheme, keep the Users call for the profile claims, prove it in `Tests/Gateway/GatewayTests`..."

**Chairman's Verdict:**

*Where the council agrees:* The per-request hop is worth removing or shrinking.

*Where the council clashes:* Revocation versus availability. The Contrarian wants Users to stay the authority on every request; the Expansionist wants the gateway to survive Users being down.

*Blind spots caught:* The profile claims aren't in the token, so "remove the hop" is really "shrink what the gateway asks Users for."

*Recommendation:* Validate signature and expiry at the gateway; decide separately whether profile claims belong in the token. Update the `api-gateway` and `rpc` skills with the outcome.

*One thing to do first:* Add a gateway test that a token with a bad signature is rejected without calling Users.Api.

---

## important notes

- **Always spawn all 5 advisors in parallel.** Sequential spawning wastes time and lets earlier responses bleed into later ones.
- **Always anonymize for peer review.** If reviewers know which advisor said what, they'll defer to certain thinking styles instead of evaluating on merit.
- **The chairman can disagree with the majority.** If 4 out of 5 advisors say "do it" but the reasoning of the 1 dissenter is strongest, the chairman should side with the dissenter and explain why.
- **Don't council trivial questions.** If the user asks something with one right answer, just answer it. If an ADR or skill already decides it, answer from that file.
- **Advisors guess.** A recommendation that rests on a claim about the code is only as good as that claim; verify it before presenting.
- **The verdict lives in chat.** Most users will scan it, not read the advisors' full answers. Keep it clean and scannable.
