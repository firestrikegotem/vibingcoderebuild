const fs = require('fs');
const path = require('path');
const configPath = path.join(__dirname, path.sep + 'config.json');
if (!fs.existsSync(configPath)) {
  throw new Error('Configuration could not be found at location: ' + configPath);
}
const config = JSON.parse(fs.readFileSync(configPath).toString('utf-8'));

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