const fs = require('fs');
const path = require('path');
const configPath = path.join(__dirname, path.sep + 'config.json');
// config.json is gitignored, so a fresh clone has none. Fall back to the checked-in example rather
// than refusing to start, and say so, because the example points at roblox.com.
let configPathToUse = configPath;
if (!fs.existsSync(configPath)) {
  configPathToUse = path.join(__dirname, path.sep + 'config.example.json');
  if (!fs.existsSync(configPathToUse)) {
    throw new Error('Configuration could not be found at location: ' + configPath);
  }
  console.warn('[warn] config.json is missing, using config.example.json. Copy it and adjust baseUrl for local use.');
}
const config = JSON.parse(fs.readFileSync(configPathToUse).toString('utf-8'));

module.exports = {
  reactStrictMode: true,
  serverRuntimeConfig: config.serverRuntimeConfig,
  publicRuntimeConfig: config.publicRuntimeConfig,
  
  async rewrites() {
    return [
      // FIXED: Forward ALL /apisite requests to the backend
      {
        source: '/apisite/:path*',
        destination: 'http://127.0.0.1:5000/apisite/:path*',
      },
    ];
  },

  async redirects() {
    return [
      {
        source: '/catalog.aspx',
        destination: '/catalog',
        permanent: true,
      },
      {
        source: '/groups/:id/:name',
        destination: '/My/Groups.aspx?gid=:id',
        permanent: false,
      },
    ]
  },
  webpack: (config, { buildId, dev, isServer, defaultLoaders, webpack }) => {
    return config
  },
}