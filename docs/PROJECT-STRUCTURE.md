# VoxMentor — Project Structure

> The single source of truth for how the VoxMentor repository is organized. Target layout for the Week 1 scaffold (issues #2–#4).

```
voxmentor/                              ← THE ONLY REPO
│
├── .github/
│   ├── workflows/
│   │   ├── ci.yml                      ← Build + test on every PR
│   │   ├── deploy-staging.yml          ← Auto-deploy on merge to main
│   │   └── codeql.yml                  ← Security scanning
│   ├── pull_request_template.md        ← PR template
│   └── ISSUE_TEMPLATE/
│       └── feature.yml                 ← Issue template
│
├── src/                                ← ALL .NET projects
│   ├── VoxMentor.Domain/               ← Pure C# entities (zero deps)
│   ├── VoxMentor.Application/          ← CQRS, BKT, JD Engine, selectors
│   ├── VoxMentor.Infrastructure/       ← EF Core, Redis, Ollama client
│   ├── VoxMentor.Api/                  ← Main API (monolith)
│   ├── VoxMentor.TutorService/         ← AI Tutor microservice
│   ├── VoxMentor.CodeExecService/      ← Code Execution microservice
│   ├── VoxMentor.Gateway/              ← YARP API Gateway
│   └── VoxMentor.Tests/                ← All unit + integration tests
│       ├── Unit/
│       └── Integration/
│
├── web/                               ← Next.js 14 frontend
│   ├── app/
│   ├── components/
│   ├── lib/
│   ├── public/
│   ├── Dockerfile
│   └── package.json
│
├── voice-service/                    ← Python voice service
│   ├── main.py
│   ├── Dockerfile
│   └── requirements.txt
│
├── scripts/
│   ├── seed-dsa-concepts.sql          ← 50 DSA concepts + prerequisites
│   ├── seed-questions.sql             ← 100 practice questions
│   ├── seed-ctci-chunks.py            ← Embed CTCI + GFG content
│   └── init-db.sh                     ← Create + migrate + seed
│
├── docker/
│   ├── Dockerfile.Api
│   ├── Dockerfile.Tutor
│   ├── Dockerfile.CodeExec
│   ├── Dockerfile.Gateway
│   └── Dockerfile.Web
│
├── docs/
│   ├── README.md                     ← Project overview
│   ├── ARCHITECTURE.md              ← Architecture decisions (ADRs)
│   ├── SETUP.md                     ← How to run locally
│   ├── API.md                       ← API endpoints
│   └── CHANGELOG.md                ← What changed each week
│
├── docker-compose.yml               ← ONE FILE runs everything
├── docker-compose.prod.yml          ← Production override (Railway/VPS)
├── .gitignore
├── .dockerignore
├── Directory.Packages.props         ← Central NuGet versions (production)
├── Directory.Build.props            ← Shared build settings
├── VoxMentor.sln                    ← Solution file
└── README.md
```
