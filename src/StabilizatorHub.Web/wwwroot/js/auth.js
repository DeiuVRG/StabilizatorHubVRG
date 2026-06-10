// Login / registration page logic.
import { api, ApiError } from './api.js';

const tabLogin = document.getElementById('tab-login');
const tabRegister = document.getElementById('tab-register');
const loginForm = document.getElementById('login-form');
const registerForm = document.getElementById('register-form');
const errorBox = document.getElementById('form-error');

function showTab(login) {
  tabLogin.classList.toggle('active', login);
  tabRegister.classList.toggle('active', !login);
  loginForm.classList.toggle('hidden', !login);
  registerForm.classList.toggle('hidden', login);
  hideError();
}

function showError(message) {
  errorBox.textContent = message;
  errorBox.classList.add('visible');
}

function hideError() {
  errorBox.classList.remove('visible');
}

async function submit(button, action) {
  hideError();
  const original = button.textContent;
  button.disabled = true;
  button.textContent = 'Please wait...';

  try {
    await action();
    location.href = '/';
  } catch (err) {
    showError(err instanceof ApiError ? err.message : 'Could not reach the server. Try again.');
  } finally {
    button.disabled = false;
    button.textContent = original;
  }
}

tabLogin.addEventListener('click', () => showTab(true));
tabRegister.addEventListener('click', () => showTab(false));

loginForm.addEventListener('submit', event => {
  event.preventDefault();
  submit(loginForm.querySelector('button[type=submit]'), () =>
    api('/api/auth/login', {
      method: 'POST',
      body: {
        email: document.getElementById('login-email').value.trim(),
        password: document.getElementById('login-password').value
      }
    }));
});

registerForm.addEventListener('submit', event => {
  event.preventDefault();
  submit(registerForm.querySelector('button[type=submit]'), () =>
    api('/api/auth/register', {
      method: 'POST',
      body: {
        email: document.getElementById('reg-email').value.trim(),
        password: document.getElementById('reg-password').value,
        pairingCode: document.getElementById('reg-code').value.trim().toUpperCase()
      }
    }));
});

// Already signed in? Go straight to the dashboard.
api('/api/auth/me').then(() => { location.href = '/'; }).catch(() => { /* stay */ });
