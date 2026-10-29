import request from "../lib/request";
import { getFullUrl } from "../lib/request";

export const login = ({ username, password }) => {
  // FIXED: Backend expects 'cvalue' for the username
  return request('POST', '/apisite/auth/v2/login', {
    ctype: 'username',
    cvalue: username,
    password: password
  });
};

export const logout = () => {
  // FIXED: Use the exact same path as login ('auth/v2' instead of 'authentication')
  return request('POST', '/apisite/auth/v2/logout', {});
}

export const changePassword = ({ existingPassword, newPassword }) => {
  return request('POST', getFullUrl('authentication', `/v2/user/passwords/change`), {
    currentPassword: existingPassword,
    newPassword,
  });
}

export const validateUsername = ({ username, context }) => {
  return request('GET', getFullUrl('authentication', `/v1/usernames/validate?username=${encodeURIComponent(username)}&context=${encodeURIComponent(context)}`)).then(d => d.data)
}

export const changeUsername = ({ username, password }) => {
  return request('POST', getFullUrl('authentication', `/v1/username`), {
    username,
    password,
  })
}

export const logoutFromAllOtherSessions = () => {
  return request('POST', getFullUrl('authentication', '/v2/logoutfromallsessionsandreauthenticate'))
}