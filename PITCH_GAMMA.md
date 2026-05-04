# DiReCo — Disaster Response Coordinator
### An Autonomous AI Agent for Emergency Response in Uganda
**AIFEST 2026 · Top 10 Pitch · 9th May 2026 · American Center, Kampala**

Tagline: *An AI dispatcher that thinks, decides and acts — in seconds, offline, in any language.*

---

## The Problem

**In Uganda, help arrives too late — when it arrives at all.**

- 🌍 Floods, landslides, fires and medical emergencies kill thousands every year — Kasese, Bududa, Mbale, Kampala slums
- 📞 911/999 is fragmented across Police, Red Cross, hospitals and district DDMCs — no single coordinated dispatcher
- 📵 Victims in rural areas have **patchy 2G/3G**, often no data, sometimes no airtime
- ⏱️ Average emergency response time in rural Uganda: **45+ minutes** — global best practice is under 8
- 🗺️ Responders show up **without context**: no location, no severity, no medical history, no victim count

**Result:** preventable deaths, duplicated efforts, and citizens who simply stop calling for help.

---

## The Solution — DiReCo, an Autonomous Response Agent

**Not a chatbot. An AI agent that perceives, reasons, decides and acts on behalf of the citizen.**

The agent runs a **LangChain ReAct loop** orchestrating Gemini + a toolbelt of real-world actions:

- 🧠 **Perceive** — ingests SOS type, GPS, victim chat, weather, facility availability
- 🤔 **Reason** — Gemini decides severity, required responder type, survival instructions
- 🛠️ **Act** — autonomously calls tools: `assign_facility`, `send_sms`, `send_whatsapp`, `notify_responder`, `geocode`, `lookup_hospital`, `escalate_to_admin`
- 🔁 **Loop** — re-plans as the victim replies ("I'm trapped under concrete" → re-classifies to Critical, dispatches heavy rescue, sends survival steps)

**Citizen-facing channels (all driven by the same agent):**
- 🆘 One-tap SOS PWA — Landslide / Flood / Fire / Medical / Other
- 📴 Offline-first — IndexedDB outbox + Background Sync; the agent fires the moment connectivity returns
- 💬 WhatsApp & SMS via Africa's Talking — citizens without smartphones get the same agent
- 📍 Live admin dashboard — every agent decision is auditable in real time

**Differentiator:** Existing apps are forms. DiReCo is an **autonomous agent that takes action** — offline-capable, multi-channel, and explainable.

---

## Technology & Architecture

**Agent stack**
- 🧠 **LangChain** — ReAct agent orchestration, tool routing, memory, prompt templates
- ✨ **Google Gemini 1.5** — reasoning LLM behind the agent
- 🛠️ **Custom tool layer** — `AssignFacility`, `SendSMS`, `SendWhatsApp`, `Geocode`, `LookupHospital`, `NotifyResponder`, `EscalateToAdmin`
- 🧾 **Vector memory** — per-user chat history & disaster knowledge base for retrieval-augmented triage

**Platform stack**
- Frontend: **Blazor Server PWA** (.NET 8) with offline service worker, IndexedDB, Background Sync
- Backend: **ASP.NET Core 8 API** + SignalR for live dashboards
- Data: **PostgreSQL (Neon)** with geo queries
- Channels: **Africa's Talking SMS** + **WhatsApp Business Cloud API**
- Maps & geocoding: **Google Places + OpenStreetMap**
- Deploy: Docker on Render, edge-cached PWA

**Agent flow**
`Citizen signal (PWA / WhatsApp / SMS) → LangChain ReAct Agent → [Gemini reasons → picks tool → executes → observes] ⟲ → Auto-dispatch + survival guidance → Live audit on admin dashboard`

---

## Business Model

**Who pays**
- 🏛️ **Government & DDMCs** — annual SaaS license per district (sustainable, scalable)
- 🏥 **Hospitals, Red Cross, fire brigades** — premium tier for live dashboards & analytics
- 🛰️ **Telecoms (MTN, Airtel, Africa's Talking)** — co-branded short codes & WhatsApp shared-cost messaging
- 🌍 **NGOs & donors** — UNDP, UNICEF, World Bank disaster preparedness grants

**For citizens: 100% free, forever.**

**Revenue streams**
1. District licensing (USD 2k–10k / district / year)
2. Enterprise dashboard subscriptions
3. Telecom revenue share on SMS/WhatsApp traffic
4. Anonymized disaster analytics for insurers and reinsurers

---

## Demo / Prototype

**Live now:** `https://drc-app.onrender.com` — judges can log in and trigger a real SOS.

**What works today (MVP):**
- ✅ **LangChain ReAct agent** running live triage, autonomously calling tools
- ✅ One-tap SOS with 5 emergency types (Landslide, Flood, Fire, Medical, Other)
- ✅ Offline queueing — kill your wifi, tap SOS, turn it back on, watch the agent fire
- ✅ WhatsApp two-way chat with the same agent
- ✅ SMS inbound/outbound via Africa's Talking
- ✅ Live admin map: every agent action (assign / notify / escalate) is auditable
- ✅ Persistent multi-turn agent memory per user
- ✅ JWT auth, role-based admin, immutable audit logs

**Stage:** Working MVP, deployed on production infrastructure, ready for district pilot.

---

## Future Plans & The Ask

**Roadmap**
- 📞 IVR voice channel (call → AI speaks Luganda, Runyankole, Acholi, Swahili)
- 🛰️ Satellite fallback (Starlink / Project Kuiper) for total-blackout regions
- 🩺 Wearable integration (fall detection, heart-rate triggers)
- 🧠 On-device tiny-LLM for fully offline triage on cheap Android phones
- 🌍 Expansion: Kenya, Tanzania, Rwanda, DRC

**The Ask**
- 💰 **USD 75,000 seed** — 12-month runway for 3 engineers + 1 field officer
- 🤝 **Pilot partner** — 1 Ugandan district + 1 hospital network
- 📡 **Telco partnership** — short code + WhatsApp business sponsorship
- 🎓 **Mentorship** — go-to-market in African public sector

> *"Every minute saved is a life saved. DiReCo is the autonomous AI agent that turns Uganda's emergency response from minutes-late to seconds-early."*

---

## Team

**Peter Nuwahereza** — Founder & Full-Stack / AI Engineer
*Builds the platform end-to-end: Blazor, .NET, Gemini, Africa's Talking integration*

*(Add additional team members here)*

---

## Thank You

**DiReCo — Disaster Response Coordinator**
🌐 drc-app.onrender.com
📧 drc@africastalking.ug
📱 +256 779 081 600

**Questions?**
