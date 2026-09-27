# Portion — Frontend

The React + TypeScript + Vite single-page app for Portion AI.

Full setup, architecture and API documentation live in the
[repository README](../README.md).

## Quick start

```bash
npm install
cp .env.example .env      # optional; defaults work with the Vite dev proxy
npm run dev               # http://localhost:5173
```

`npm run dev` proxies `/api` and `/health` to `http://localhost:5000`, so the
browser never needs CORS configuration.

## Scripts

| Script            | Purpose                                     |
| ----------------- | ------------------------------------------- |
| `npm run dev`     | Vite dev server with the API proxy          |
| `npm run build`   | `tsc -b` type-check, then production build  |
| `npm run lint`    | Oxlint                                      |
| `npm run preview` | Serve the production build                  |
