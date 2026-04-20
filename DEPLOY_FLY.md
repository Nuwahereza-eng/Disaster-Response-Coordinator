# 🚀 Deploy DRC for free (Fly.io + Neon Postgres)

Total cost: **$0**. Data persists forever. No sleeping database.

| Layer          | Provider    | Free tier                               |
| -------------- | ----------- | --------------------------------------- |
| Postgres DB    | **Neon**    | 0.5 GB, no expiry, auto-wake in ~300 ms |
| API compute    | **Fly.io**  | shared-cpu 512 MB, always-on            |
| Blazor UI      | **Fly.io**  | shared-cpu 512 MB, always-on            |

Region: `jnb` (Johannesburg) — closest Fly location to Uganda.

---

## 1. Create the Postgres database (Neon, 2 min)

1. Go to <https://neon.tech> and sign up (GitHub login works).
2. **Create project** → name it `drc`, region **AWS eu-central-1** (closest to JNB).
3. Copy the **connection string** it gives you. It will look like:

   ```
   postgres://drc_owner:AbCdEf123@ep-round-moon-12345.eu-central-1.aws.neon.tech/drc?sslmode=require
   ```

> Keep this URL — you'll paste it as `DATABASE_URL` below.

The API auto-detects `DATABASE_URL` and runs `EnsureCreated()` on first boot, so Neon will be populated automatically (users, facilities, the default admin, etc.).

---

## 2. Install flyctl (1 min)

```bash
# Linux / macOS / WSL
curl -L https://fly.io/install.sh | sh
fly auth signup          # or: fly auth login
```

---

## 3. Deploy the API (3 min)

From the repo root:

```bash
# First-time launch — no deploy yet, we need to set secrets
fly launch --config fly.api.toml --no-deploy --copy-config --name drc-api --region jnb

# Set secrets (replace the values!)
fly secrets set \
  DATABASE_URL="postgres://drc_owner:...@ep-...neon.tech/drc?sslmode=require" \
  Apps__Gemini__Key="YOUR_GEMINI_API_KEY" \
  AfricasTalking__Username="YOUR_AT_USERNAME" \
  AfricasTalking__ApiKey="YOUR_AT_API_KEY" \
  AfricasTalking__SenderId="DIRECO" \
  Apps__Meta__AccessToken="YOUR_WHATSAPP_TOKEN" \
  Apps__Meta__WhatsAppBusinessPhoneNumberId="YOUR_PHONE_ID" \
  --app drc-api

# Deploy
fly deploy --config fly.api.toml --app drc-api
```

Your API is now live at **https://drc-api.fly.dev** (Swagger at `/swagger`).

Verify:

```bash
curl https://drc-api.fly.dev/alive     # → Healthy
curl https://drc-api.fly.dev/api/facilities | head
```

---

## 4. Deploy the frontend (2 min)

```bash
fly launch --config fly.app.toml --no-deploy --copy-config --name drc-app --region jnb

fly secrets set \
  ApiUrl="https://drc-api.fly.dev" \
  --app drc-app

fly deploy --config fly.app.toml --app drc-app
```

Your UI is now live at **https://drc-app.fly.dev** 🎉

---

## 5. Point Africa's Talking USSD & SMS callbacks at Fly

In the Africa's Talking sandbox / app dashboard:

| Channel | Callback URL                                        |
| ------- | --------------------------------------------------- |
| USSD    | `https://drc-api.fly.dev/api/ussd`                  |
| SMS     | `https://drc-api.fly.dev/api/webhook/sms` *(if used)* |

---

## 6. Keeping it free & warm

Fly.io free allowance (as of 2026):
- 3 shared-cpu-1x 256 MB VMs always-on **OR** 3 × 512 MB with occasional auto-stop
- 160 GB outbound bandwidth / month

The TOMLs in this repo request `512 MB` + `min_machines_running=1`. If you bump into the free-tier ceiling, you can temporarily flip `auto_stop_machines = "suspend"` — Fly will wake the machine from RAM snapshot in ~1 s (much faster than Render's cold start).

Neon auto-suspends the DB after 5 min idle (still free, wakes in <1 s on the next query). The API's connection pool re-dials cleanly.

---

## 7. Useful flyctl commands

```bash
fly logs  --app drc-api              # tail API logs
fly logs  --app drc-app              # tail UI logs
fly ssh console --app drc-api        # shell into the running container
fly status --app drc-api             # health, machine count, region
fly scale memory 1024 --app drc-api  # bump RAM if Gemini timeouts
fly secrets list --app drc-api       # what's currently set (values hidden)
```

---

## 8. Rollback / redeploy

```bash
fly releases --app drc-api           # list releases
fly deploy --image <previous-image>  # rollback
```

That's it — judges can hit your live URL any time during the finals weekend, database included. ✅
