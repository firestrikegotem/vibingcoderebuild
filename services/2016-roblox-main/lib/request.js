import axios from 'axios';
import config from '../lib/config';
let _csrf = '';

// HARDCODED BACKEND URL
const BACKEND_URL = 'http://localhost:5000';

const getFullUrl = (apiSite, fullUrl) => {
  return `${BACKEND_URL}/apisite/${apiSite}${fullUrl}`;
}

const getBaseUrl = () => {
  return BACKEND_URL;
}

const getUrlWithProxy = (url) => {
  return url;
}

const request = async (method, url, data) => {
  const isBrowser = typeof window !== 'undefined';
  try {
    let headers = {
      'x-csrf-token': _csrf,
    }
    if (!isBrowser) {
      const authHeaderValue = config.serverRuntimeConfig.backend.authorization;
      if (typeof authHeaderValue === 'string')
        headers[config.serverRuntimeConfig.backend.authorizationHeader || 'authorization'] = authHeaderValue;
      headers['user-agent'] = 'Roblox2016/1.0';
    }
    
    // Ensure the URL is absolute and valid
    let finalUrl = getUrlWithProxy(url);
    
    if (!finalUrl || typeof finalUrl !== 'string') {
      console.error("[FATAL] request.js received an invalid URL:", finalUrl, "Original url:", url);
      throw new Error("Invalid URL passed to request");
    }

    // Clean up any invisible spaces or newlines
    finalUrl = finalUrl.trim();

    if (finalUrl.startsWith('/')) {
      finalUrl = BACKEND_URL + finalUrl;
    }

    // Encode the URL to prevent XMLHttpRequest "Invalid URL" errors
    finalUrl = encodeURI(finalUrl);

    console.log(`[request.js] Fetching: ${method} ${finalUrl}`); // LOGS THE EXACT URL

    const result = await axios.request({
      method,
      url: finalUrl,
      data: data,
      headers: headers,
      maxRedirects: 0,
      withCredentials: true, 
    });
    return result;
  } catch (e) {
    if (e.response) {
      let resp = e.response;
      if (resp.status === 403 && resp.headers['x-csrf-token']) {
        _csrf = resp.headers['x-csrf-token'];
        return await request(method, url, data);
      }
    }
    if (isBrowser) {
      if (e.response && e.response.data && e.response.data.errors && e.response.data.errors.length) {
        let err = e.response.data.errors[0]
        e.message = e.message + ': ' + (err.code + ': ' + err.message);
      }
      throw e;
    } else {
      throw new Error(e.message);
    }
  }
}

export default request;

export {
  getFullUrl,
  getBaseUrl,
  getUrlWithProxy,
}