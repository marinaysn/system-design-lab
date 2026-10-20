// Dev proxy: forwards /api to the ASP.NET Core backend so no CORS changes are needed.
// Change the target without touching app code:  $env:API_TARGET = "https://localhost:5001"
// Direct to one backend (default): https://localhost:5001
// Through the YARP reverse proxy (3 instances, Phase 4): $env:API_TARGET = "https://localhost:5100"
// The YARP affinity cookie (routing only) is passed through unchanged; browsers accept its
// Secure attribute on http://localhost.
// TLS verification stays ON. The backend uses the ASP.NET dev certificate, so run
// `dotnet dev-certs https --trust` once and start Angular with `npm start`, which makes
// Node trust the operating-system certificate store (--use-system-ca).
module.exports = {
  '/api': {
    target: process.env.API_TARGET || 'https://localhost:5001',
    secure: true,
    changeOrigin: true,
  },
};
