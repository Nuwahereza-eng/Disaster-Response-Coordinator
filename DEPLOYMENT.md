# 🚀 Free Deployment Guide - Disaster Response Coordinator

This guide covers deploying the DRC application for **FREE** using various cloud platforms.

## 📋 Prerequisites

Before deploying, you'll need:
- A GitHub account (push your code to GitHub)
- API keys for external services:
  - **Gemini API Key**: Get from [Google AI Studio](https://aistudio.google.com/app/apikey)
  - **Meta WhatsApp API**: From [Meta for Developers](https://developers.facebook.com/)

---

## 🏆 Option 1: Render.com (Recommended - Easiest)

**Free Tier Includes:**
- ✅ 750 hours/month of web services
- ✅ Free Redis (25MB)
- ✅ Auto-deploy from GitHub
- ✅ Free SSL certificates

### Steps:

1. **Push to GitHub**
   ```bash
   git add .
   git commit -m "Add deployment configuration"
   git push origin main
   ```

2. **Deploy via Blueprint**
   - Go to [Render Dashboard](https://dashboard.render.com)
   - Click **New** → **Blueprint**
   - Connect your GitHub repository
   - Render will auto-detect `render.yaml` and deploy all services

3. **Set Environment Variables**
   After deployment, go to each service and set:
   
   **For drc-api:**
   ```
   Apps__Gemini__Key=your-gemini-api-key
   Apps__Meta__AccessToken=your-meta-access-token
   Apps__Meta__Key=your-webhook-verify-token
   Apps__Meta__WhatsAppBusinessAccountId=your-account-id
   Apps__Meta__WhatsAppBusinessPhoneNumberId=your-phone-id
   JWT_SECRET_KEY=your-super-secret-jwt-key-min-32-chars
   ```

4. **Your URLs will be:**
   - API: `https://drc-api.onrender.com`
   - App: `https://drc-app.onrender.com`

---

## 🚂 Option 2: Railway.app

**Free Tier Includes:**
- ✅ $5 free credit/month
- ✅ Easy Docker deployment
- ✅ Built-in databases

### Steps:

1. **Install Railway CLI**
   ```bash
   npm install -g @railway/cli
   railway login
   ```

2. **Create a new project**
   ```bash
   railway init
   ```

3. **Add Redis**
   - In Railway dashboard, click **+ New** → **Database** → **Redis**

4. **Deploy API**
   ```bash
   cd DRC.Api
   railway up
   ```

5. **Deploy App**
   ```bash
   cd ../DRC.App
   railway up
   ```

6. **Set environment variables** in Railway dashboard for each service.

---

## 🪰 Option 3: Fly.io

**Free Tier Includes:**
- ✅ 3 shared-cpu VMs (256MB RAM each)
- ✅ 160GB outbound bandwidth
- ✅ Free SSL

### Steps:

1. **Install Fly CLI**
   ```bash
   curl -L https://fly.io/install.sh | sh
   fly auth login
   ```

2. **Deploy API**
   ```bash
   fly launch --config fly.api.toml --dockerfile DRC.Api/Dockerfile
   ```

3. **Add Redis (via Upstash - Free)**
   ```bash
   fly redis create
   ```

4. **Set secrets**
   ```bash
   fly secrets set GEMINI_API_KEY=your-key -a drc-api
   fly secrets set META_ACCESS_TOKEN=your-token -a drc-api
   fly secrets set JWT_SECRET_KEY=your-jwt-secret -a drc-api
   ```

5. **Deploy App**
   ```bash
   fly launch --config fly.app.toml --dockerfile DRC.App/Dockerfile
   ```

6. **Link API to App**
   ```bash
   fly secrets set API_URL=https://drc-api.fly.dev -a drc-app
   ```

---

## 🔧 Environment Variables Reference

| Variable | Description | Required |
|----------|-------------|----------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` | Yes |
| `Apps__Gemini__Key` | Gemini AI API key | Yes |
| `Apps__Meta__AccessToken` | WhatsApp API access token | Yes |
| `Apps__Meta__Key` | Webhook verification token | Yes |
| `Apps__Meta__WhatsAppBusinessAccountId` | WhatsApp Business Account ID | Yes |
| `Apps__Meta__WhatsAppBusinessPhoneNumberId` | WhatsApp Phone Number ID | Yes |
| `ConnectionStrings__redis` | Redis connection string | Yes |
| `JWT_SECRET_KEY` | JWT signing key (min 32 chars) | Yes |
| `services__api__http__0` | API URL for frontend | Yes (App only) |

---

## 🆓 Free Redis Options

If your platform doesn't include Redis:

1. **Upstash** (Recommended)
   - 10,000 commands/day free
   - [upstash.com](https://upstash.com)

2. **Redis Cloud**
   - 30MB free
   - [redis.com/try-free](https://redis.com/try-free/)

---

## 📱 WhatsApp Webhook Configuration

After deployment, configure your WhatsApp webhook:

1. Go to [Meta for Developers](https://developers.facebook.com/)
2. Navigate to your app → WhatsApp → Configuration
3. Set Webhook URL: `https://your-api-url.com/api/webhook`
4. Set Verify Token: Same as `Apps__Meta__Key`
5. Subscribe to: `messages`

---

## 🔍 Troubleshooting

### App won't start
- Check logs: `fly logs -a drc-api` or in platform dashboard
- Ensure all required env vars are set
- Check health endpoint: `curl https://your-api.com/health`

### Redis connection fails
- Verify `ConnectionStrings__redis` format
- For Upstash: Use the REST URL with password

### WhatsApp webhook not working
- Verify the webhook URL is accessible
- Check verify token matches
- Ensure API is responding to GET requests for verification

---

## 💡 Tips for Free Tier

1. **Render**: Services sleep after 15 mins of inactivity. First request takes ~30s to wake.
2. **Railway**: Monitor your $5 credit usage in the dashboard.
3. **Fly.io**: Use `auto_stop_machines = true` to save resources.

---

## 🎉 Success!

Your Disaster Response Coordinator should now be live and FREE!

- 🌐 **Frontend**: `https://drc-app.{platform}.com`
- 🔌 **API**: `https://drc-api.{platform}.com`
- 📚 **Swagger**: `https://drc-api.{platform}.com/swagger` (if enabled)
