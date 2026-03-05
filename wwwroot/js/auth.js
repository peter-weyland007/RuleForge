window.ruleforgeAuth = {
  async login(identifier, password) {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ identifier, password })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Login failed');
    return text ? JSON.parse(text) : {};
  },

  async register(email, username, password) {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ email, username, password })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Registration failed');
    return text ? JSON.parse(text) : {};
  },

  async me() {
    const res = await fetch('/api/auth/me', { credentials: 'include' });
    if (res.status === 401) return null;
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to fetch profile');
    return text ? JSON.parse(text) : null;
  },

  async logout() {
    const res = await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Logout failed');
    return text ? JSON.parse(text) : {};
  },

  async changePassword(currentPassword, newPassword) {
    const res = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ currentPassword, newPassword })
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Password change failed');
    return text ? JSON.parse(text) : {};
  },

  async adminUsers() {
    const res = await fetch('/api/admin/users', { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load users');
    return text ? JSON.parse(text) : [];
  },

  async adminUser(id) {
    const res = await fetch(`/api/admin/users/${id}`, { credentials: 'include' });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to load user');
    return text ? JSON.parse(text) : null;
  },

  async adminCreateUser(payload) {
    const res = await fetch('/api/admin/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to create user');
    return text ? JSON.parse(text) : {};
  },

  async adminUpdateUser(id, payload) {
    const res = await fetch(`/api/admin/users/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    if (!res.ok) throw new Error(text || 'Failed to update user');
    return text ? JSON.parse(text) : {};
  }
};
