<div align="center">

# 🚀 DailyReview.ai

### AI-Powered Code Review Agent for Everyday Pull Requests

Automatically review GitHub Pull Requests using AI and receive actionable inline review comments—right where developers already work.

**Built for the OpenAI ChatGPT Codex Hackathon 2026**

[🔗 GitHub App](https://github.com/apps/dailyreview-ai)

</div>

---

## 📖 Overview

DailyReview.ai is a GitHub App that automatically reviews Pull Requests using Large Language Models (LLMs) and posts inline review comments directly on GitHub.

Instead of replacing human reviewers, it acts as an intelligent first reviewer that catches common issues such as:

- 🐛 Potential bugs
- 🔒 Security concerns
- ⚠️ Missing null checks
- 🧠 Logic mistakes
- ✨ Code quality improvements
- 📚 Best practice recommendations

The goal is to provide **fast, affordable, AI-powered code reviews** for everyday development.

---

## ❓ Why DailyReview.ai

Traditional code quality tools are powerful but often come with fixed licensing costs that don't scale well for routine development.

DailyReview.ai introduces a flexible alternative:

- ✅ Review every Pull Request automatically
- ✅ Use free or premium AI models based on your needs
- ✅ No vendor lock-in
- ✅ Works directly inside GitHub
- ✅ Provider-agnostic architecture

---

## ✨ Features

- 🤖 Automatic AI review for every Pull Request
- 💬 Posts inline comments directly on GitHub
- 🔄 Supports multiple LLM providers
- 🌍 OpenAI-compatible architecture
- 📂 Diff-based review (fast & efficient)
- 🛡️ AI response validation before posting
- 🧠 Language-aware prompts
- 🔌 Zero additional UI — everything happens inside GitHub

---

## 🏗️ Architecture

```
Developer
     │
     ▼
GitHub Pull Request
     │
     ▼
 GitHub Webhook
     │
     ▼
DailyReview.ai
     │
 ├── Build Diff Context
 ├── Detect Language
 ├── Generate Prompt
 ├── Call AI Provider
 ├── Validate Response
 └── Post Review Comments
     │
     ▼
GitHub Pull Request
```

---

## ⚙️ Tech Stack

### Backend

- ASP.NET Core (.NET 10)
- Clean Architecture
- REST API

### AI Providers

- Groq
- Google Gemini
- Any OpenAI-compatible provider

### GitHub

- GitHub App
- JWT Authentication
- Installation Tokens
- GitHub REST API

### Hosting

- Render

---

## 🧠 Built with ChatGPT Codex

This project was developed using **ChatGPT Codex** as an AI engineering partner.

Codex assisted with:

- Architecture planning
- GitHub App implementation
- JWT authentication
- Diff parsing
- Prompt engineering
- AI response parsing
- Validation layer
- Debugging production issues
- Iterative feature development

Rather than generating the project in a single prompt, the application was built incrementally through task-focused development and continuous testing.

---

## 🔍 How It Works

1. Install the GitHub App.
2. Open or update a Pull Request.
3. GitHub sends a webhook.
4. DailyReview.ai fetches the PR diff.
5. A language-aware prompt is generated.
6. The selected AI model reviews the changes.
7. Responses are validated.
8. Inline comments are posted back to GitHub.

---

## 📈 Why It Is Different

| Traditional Tools | DailyReview.ai |
|-------------------|----------------|
| Fixed licensing | Pay only for AI usage |
| Vendor locked | Any OpenAI-compatible provider |
| Same review cost for every PR | Flexible model selection |
| Heavy static analysis | Lightweight AI-first review |

---

## 🚧 Current Limitations

- Diff-scoped review only
- GitHub support only
- Shared demo API key
- LLM responses can occasionally require human verification

---

## 🔮 Future Roadmap

- Multi-file semantic analysis
- GitLab support
- Bitbucket integration
- Per-organization API keys
- Model routing based on PR complexity
- Team dashboards
- Review history & analytics



---

## 🔗 GitHub App

https://github.com/apps/dailyreview-ai

---

## 🎥 Demo Video

https://drive.google.com/drive/folders/1iePOVPQtn2puV0yqFvNl4PmzQtCGCl44

---

## 🤝 Built For

**OpenAI ChatGPT Codex Hackathon 2026**

Track: **Agentic Coding**

---

## ⭐ If you like this project

Give the repository a ⭐ and share your feedback!
