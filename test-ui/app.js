const STORAGE_KEYS = {
  apiBaseUrl: "authservice.testui.apiBaseUrl",
  accessToken: "authservice.testui.accessToken",
  refreshToken: "authservice.testui.refreshToken",
  googleClientId: "authservice.testui.googleClientId",
  lastEmail: "authservice.testui.lastEmail",
};

const state = {
  apiBaseUrl: localStorage.getItem(STORAGE_KEYS.apiBaseUrl) || "http://localhost:5091",
  accessToken: localStorage.getItem(STORAGE_KEYS.accessToken) || "",
  refreshToken: localStorage.getItem(STORAGE_KEYS.refreshToken) || "",
  googleClientId: localStorage.getItem(STORAGE_KEYS.googleClientId) || "",
  lastEmail: localStorage.getItem(STORAGE_KEYS.lastEmail) || "",
  prefillVerifyToken: "",
  prefillResetToken: "",
};

const routes = {
  login: {
    title: "Login",
    subtitle: "Ingreso local con email y contrasena",
    auth: false,
    render: renderLogin,
  },
  register: {
    title: "Registro",
    subtitle: "Alta de cuenta local",
    auth: false,
    render: renderRegister,
  },
  google: {
    title: "Login Google",
    subtitle: "Flujo OAuth con ID Token",
    auth: false,
    render: renderGoogle,
  },
  forgot: {
    title: "Olvide clave",
    subtitle: "Solicitar enlace de recuperacion",
    auth: false,
    render: renderForgot,
  },
  reset: {
    title: "Reset clave",
    subtitle: "Cambiar password con token de email",
    auth: false,
    render: renderReset,
  },
  verify: {
    title: "Verificar email",
    subtitle: "Confirmacion de cuenta",
    auth: false,
    render: renderVerify,
  },
  "verify-result": {
    title: "Resultado de verificacion",
    subtitle: "Estado final de confirmacion de email",
    auth: false,
    render: renderVerifyResult,
  },
  resend: {
    title: "Reenviar verificacion",
    subtitle: "Solicitar un nuevo token de verificacion",
    auth: false,
    render: renderResend,
  },
  health: {
    title: "Health",
    subtitle: "Chequeo de dependencias del backend",
    auth: false,
    render: renderHealth,
  },
  "app/home": {
    title: "Inicio",
    subtitle: "Resumen de sesion autenticada",
    auth: true,
    render: renderAppHome,
  },
  "app/me": {
    title: "Mi perfil",
    subtitle: "GET /auth/me",
    auth: true,
    render: renderAppMe,
  },
  "app/sessions": {
    title: "Sesiones",
    subtitle: "Gestion de sesiones activas",
    auth: true,
    render: renderAppSessions,
  },
  "app/security": {
    title: "Seguridad",
    subtitle: "Cambio de contrasena",
    auth: true,
    render: renderAppSecurity,
  },
  "app/tokens": {
    title: "Tokens y logout",
    subtitle: "Refresh, cierre de sesion y logout global",
    auth: true,
    render: renderAppTokens,
  },
};

const dom = {};
let flashTimeout = null;
let verifyRedirectTimer = null;

document.addEventListener("DOMContentLoaded", bootstrap);
window.addEventListener("hashchange", renderRoute);

function bootstrap() {
  dom.apiBaseUrl = byId("api-base-url");
  dom.saveConfigBtn = byId("save-config-btn");
  dom.clearSessionBtn = byId("clear-session-btn");
  dom.authPill = byId("auth-pill");
  dom.userPill = byId("user-pill");
  dom.routeTitle = byId("route-title");
  dom.routeSubtitle = byId("route-subtitle");
  dom.flash = byId("flash");
  dom.view = byId("view");
  dom.httpMeta = byId("http-meta");
  dom.httpBody = byId("http-body");
  dom.copyConsoleBtn = byId("copy-console-btn");
  dom.clearConsoleBtn = byId("clear-console-btn");

  dom.apiBaseUrl.value = state.apiBaseUrl;
  bindStaticUi();
  updateSessionUi();

  if (!location.hash) {
    location.hash = state.accessToken ? "#/app/home" : "#/login";
  }

  renderRoute();
}

function bindStaticUi() {
  document.querySelectorAll(".menu-link").forEach((button) => {
    button.addEventListener("click", () => {
      const target = button.dataset.route;
      if (!target) return;

      if (button.classList.contains("menu-private") && !state.accessToken) {
        showFlash("Debes iniciar sesion para abrir pantallas privadas.", "error");
        location.hash = "#/login";
        return;
      }

      location.hash = "#/" + target;
    });
  });

  dom.saveConfigBtn.addEventListener("click", saveApiBaseUrl);
  dom.clearSessionBtn.addEventListener("click", () => {
    clearSession();
    showFlash("Sesion local eliminada.", "ok");
    location.hash = "#/login";
  });

  dom.copyConsoleBtn.addEventListener("click", async () => {
    await copyToClipboard(dom.httpBody.textContent);
    showFlash("Consola copiada.", "ok");
  });

  dom.clearConsoleBtn.addEventListener("click", () => {
    dom.httpMeta.textContent = "Sin llamadas aun";
    dom.httpBody.textContent = "Ejecuta un formulario y aqui veras la respuesta JSON.";
  });

  dom.apiBaseUrl.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      saveApiBaseUrl();
    }
  });
}

function renderRoute() {
  if (verifyRedirectTimer) {
    clearTimeout(verifyRedirectTimer);
    verifyRedirectTimer = null;
  }

  const { path, params } = getHashState();
  let route = routes[path];

  if (!route) {
    route = routes.login;
    location.hash = "#/login";
    return;
  }

  if (route.auth && !state.accessToken) {
    showFlash("Tu sesion no existe o expiro. Inicia sesion nuevamente.", "error");
    location.hash = "#/login";
    return;
  }

  toggleSpecialLayout(path);
  dom.routeTitle.textContent = route.title;
  dom.routeSubtitle.textContent = route.subtitle;
  setActiveMenu(path);
  route.render(params);
}

function renderLogin() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Bienvenido</h3>
          <p>Usa tus credenciales locales para iniciar sesion.</p>
        </div>
      </div>
      <form id="login-form" class="card">
        <div class="grid-2">
          <div class="field">
            <label for="login-email">Email</label>
            <input id="login-email" type="email" placeholder="correo@ejemplo.com" required />
          </div>
          <div class="field">
            <label for="login-password">Contrasena</label>
            <input id="login-password" type="password" placeholder="********" required />
          </div>
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Iniciar sesion</button>
        </div>
        <p class="link-row">
          <span class="route-link" data-link="register">No tienes cuenta? Registrate</span>
          <span>|</span>
          <span class="route-link" data-link="forgot">Olvidaste la clave?</span>
          <span>|</span>
          <span class="route-link" data-link="google">Entrar con Google</span>
        </p>
      </form>
    </section>
    `,
    () => {
      const form = byId("login-form");
      const emailInput = byId("login-email");
      const passwordInput = byId("login-password");

      if (state.lastEmail) emailInput.value = state.lastEmail;

      form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const email = emailInput.value.trim();
        const password = passwordInput.value;

        state.lastEmail = email;
        localStorage.setItem(STORAGE_KEYS.lastEmail, email);

        const result = await apiCall("POST", "/auth/login", {
          email,
          password,
        });

        if (!result.ok) return;

        const tokens = getTokensFromPayload(result.data);
        if (!tokens) {
          showFlash("Login respondio sin tokens esperados.", "error");
          return;
        }

        setSession(tokens.accessToken, tokens.refreshToken);
        showFlash("Login exitoso.", "ok");
        location.hash = "#/app/home";
      });
    }
  );
}

function renderRegister() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Crear cuenta</h3>
          <p>Registro local para tu microservicio de autenticacion.</p>
        </div>
      </div>
      <form id="register-form" class="card">
        <div class="grid-2">
          <div class="field">
            <label for="register-name">Nombre</label>
            <input id="register-name" type="text" placeholder="Juan Perez" />
          </div>
          <div class="field">
            <label for="register-email">Email</label>
            <input id="register-email" type="email" placeholder="correo@ejemplo.com" required />
          </div>
        </div>
        <div class="field">
          <label for="register-password">Contrasena</label>
          <input id="register-password" type="password" placeholder="Minimo 8 caracteres" required />
        </div>
        <div class="field">
          <label for="register-password-confirm">Confirmar contrasena</label>
          <input id="register-password-confirm" type="password" placeholder="Repite la contrasena" required />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Registrar</button>
          <span class="route-link" data-link="login">Ya tienes cuenta? Login</span>
        </div>
      </form>
      <div id="register-result" class="result-box hidden"></div>
    </section>
    `,
    () => {
      const form = byId("register-form");
      const resultBox = byId("register-result");

      if (state.lastEmail) byId("register-email").value = state.lastEmail;

      form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const password = byId("register-password").value;
        const passwordConfirm = byId("register-password-confirm").value;
        if (password !== passwordConfirm) {
          showFlash("Las contrasenas no coinciden.", "error");
          return;
        }

        const payload = {
          nombre: byId("register-name").value.trim(),
          email: byId("register-email").value.trim(),
          password,
          passwordConfirmacion: passwordConfirm,
        };

        state.lastEmail = payload.email;
        localStorage.setItem(STORAGE_KEYS.lastEmail, payload.email);

        const result = await apiCall("POST", "/auth/register", payload);
        if (!result.ok) return;

        const devToken = extractTokenFromUrl(result.data?.verificar_url_dev);
        if (devToken) state.prefillVerifyToken = devToken;

        let html = `
          <strong>${escapeHtml(result.data?.message || "Usuario registrado.")}</strong>
          <div class="form-actions" style="margin-top:10px;">
            <button type="button" class="btn btn-soft" id="register-resend-btn">Reenviar verificacion</button>
          </div>
        `;
        if (devToken) {
          html += `
            <p class="hint">Tu backend esta en Development y devolvio un token de verificacion.</p>
            <div class="form-actions">
              <button type="button" class="btn btn-soft" id="go-verify-dev-btn">Usar token en pantalla Verify</button>
            </div>
          `;
        }

        resultBox.innerHTML = html;
        resultBox.classList.remove("hidden");

        const verifyBtn = byId("go-verify-dev-btn");
        if (verifyBtn) {
          verifyBtn.addEventListener("click", () => {
            location.hash = "#/verify";
          });
        }

        const resendBtn = byId("register-resend-btn");
        if (resendBtn) {
          resendBtn.addEventListener("click", async () => {
            const resendResult = await apiCall("POST", "/auth/resend-verification", {
              email: payload.email,
            });
            if (!resendResult.ok) return;

            const resendDevToken = extractTokenFromUrl(resendResult.data?.verificar_url_dev);
            if (resendDevToken) state.prefillVerifyToken = resendDevToken;

            showFlash("Solicitud de reenvio procesada. Revisa tu correo.", "ok");
          });
        }

        showFlash("Registro procesado.", "ok");
      });
    }
  );
}
function renderGoogle() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Acceso con Google</h3>
          <p>Valida ID Token y reutiliza el mismo esquema de tokens del login local.</p>
        </div>
      </div>
      <div class="card">
        <div class="field">
          <label for="google-client-id">Google Client ID</label>
          <input id="google-client-id" type="text" placeholder="xxxx.apps.googleusercontent.com" />
        </div>
        <div class="form-actions">
          <button id="google-init-btn" class="btn btn-soft" type="button">Inicializar Google Sign-In</button>
        </div>
        <div id="google-button-host"></div>
      </div>
      <form id="google-manual-form" class="card">
        <div class="field">
          <label for="google-id-token">ID Token manual</label>
          <textarea id="google-id-token" placeholder="Pega aqui un ID Token para POST /auth/google"></textarea>
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Enviar ID Token</button>
          <span class="route-link" data-link="login">Volver a login</span>
        </div>
      </form>
    </section>
    `,
    () => {
      const clientIdInput = byId("google-client-id");
      const manualForm = byId("google-manual-form");
      const initButton = byId("google-init-btn");
      clientIdInput.value = state.googleClientId;

      initButton.addEventListener("click", () => {
        const clientId = clientIdInput.value.trim();
        if (!clientId) {
          showFlash("Ingresa el Client ID antes de inicializar Google.", "error");
          return;
        }

        state.googleClientId = clientId;
        localStorage.setItem(STORAGE_KEYS.googleClientId, clientId);
        initGoogleSignIn(clientId);
      });

      manualForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        await submitGoogleToken(byId("google-id-token").value.trim());
      });
    }
  );
}

function renderForgot() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Recuperar contrasena</h3>
          <p>Envia una solicitud para generar token de reset.</p>
        </div>
      </div>
      <form id="forgot-form" class="card">
        <div class="field">
          <label for="forgot-email">Email</label>
          <input id="forgot-email" type="email" placeholder="correo@ejemplo.com" required />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Enviar solicitud</button>
          <span class="route-link" data-link="login">Volver a login</span>
        </div>
      </form>
      <div id="forgot-result" class="result-box hidden"></div>
    </section>
    `,
    () => {
      const form = byId("forgot-form");
      const resultBox = byId("forgot-result");
      if (state.lastEmail) byId("forgot-email").value = state.lastEmail;

      form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const email = byId("forgot-email").value.trim();
        state.lastEmail = email;
        localStorage.setItem(STORAGE_KEYS.lastEmail, email);

        const result = await apiCall("POST", "/auth/forgot-password", { email });
        if (!result.ok) return;

        const devToken = extractTokenFromUrl(result.data?.reset_url_dev);
        if (devToken) state.prefillResetToken = devToken;

        let html = `<strong>${escapeHtml(result.data?.message || "Solicitud procesada.")}</strong>`;
        if (devToken) {
          html += `
            <p class="hint">Se detecto reset_url_dev en modo Development.</p>
            <div class="form-actions">
              <button type="button" class="btn btn-soft" id="go-reset-dev-btn">Abrir pantalla Reset con token</button>
            </div>
          `;
        }

        resultBox.innerHTML = html;
        resultBox.classList.remove("hidden");

        const resetBtn = byId("go-reset-dev-btn");
        if (resetBtn) {
          resetBtn.addEventListener("click", () => {
            location.hash = "#/reset";
          });
        }
      });
    }
  );
}

function renderReset(params) {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Reset de contrasena</h3>
          <p>Aplica una nueva clave usando token de recuperacion.</p>
        </div>
      </div>
      <form id="reset-form" class="card">
        <div class="field">
          <label for="reset-token">Token</label>
          <input id="reset-token" type="text" placeholder="Token recibido por email" required />
        </div>
        <div class="field">
          <label for="reset-password">Nueva contrasena</label>
          <input id="reset-password" type="password" placeholder="Nueva clave" required />
        </div>
        <div class="field">
          <label for="reset-password-confirm">Confirmar nueva contrasena</label>
          <input id="reset-password-confirm" type="password" placeholder="Repetir nueva clave" required />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Actualizar contrasena</button>
          <span class="route-link" data-link="login">Volver a login</span>
        </div>
      </form>
    </section>
    `,
    () => {
      const form = byId("reset-form");
      const tokenInput = byId("reset-token");

      const paramToken = params.get("token");
      if (paramToken) tokenInput.value = paramToken;
      else if (state.prefillResetToken) tokenInput.value = state.prefillResetToken;

      form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const newPassword = byId("reset-password").value;
        const confirmation = byId("reset-password-confirm").value;
        if (newPassword !== confirmation) {
          showFlash("Las contrasenas no coinciden.", "error");
          return;
        }
        const payload = {
          token: tokenInput.value.trim(),
          newPassword,
          newPasswordConfirmacion: confirmation,
        };

        const result = await apiCall("POST", "/auth/reset-password", payload);
        if (!result.ok) return;

        showFlash("Contrasena actualizada. Ahora inicia sesion.", "ok");
        setTimeout(() => {
          location.hash = "#/login";
        }, 900);
      });
    }
  );
}

function renderVerify(params) {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Verificacion de email</h3>
          <p>Confirma la cuenta con token de registro.</p>
        </div>
      </div>
      <form id="verify-form" class="card">
        <div class="field">
          <label for="verify-token">Token</label>
          <input id="verify-token" type="text" placeholder="Token de verificacion" required />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Verificar email</button>
          <span class="route-link" data-link="login">Ir a login</span>
        </div>
      </form>
    </section>
    `,
    () => {
      const form = byId("verify-form");
      const tokenInput = byId("verify-token");

      const paramToken = params.get("token");
      if (paramToken) tokenInput.value = paramToken;
      else if (state.prefillVerifyToken) tokenInput.value = state.prefillVerifyToken;

      form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const token = tokenInput.value.trim();
        const result = await apiCall("GET", "/auth/verify-email/" + encodeURIComponent(token));
        if (!result.ok) return;
        showFlash("Email verificado. Ya puedes iniciar sesion.", "ok");
      });
    }
  );
}

function renderVerifyResult(params) {
  const status = (params.get("status") || "error").toLowerCase();
  const title = params.get("title") || (status === "success" ? "Email verificado" : "Verificación fallida");
  const message = params.get("message") ||
    (status === "success"
      ? "Tu cuenta fue verificada correctamente."
      : "No fue posible verificar el email con el enlace recibido.");
  const isSuccess = status === "success";

  mountView(
    `
    <div class="verify-center-screen">
      <div class="verify-center-card ${isSuccess ? "is-success" : "is-error"}">
        <div class="verify-badge">${isSuccess ? "Verificación completada" : "No se pudo verificar"}</div>
        <h1>${escapeHtml(title)}</h1>
        <p class="verify-message">${escapeHtml(message)}</p>
        <p id="verify-result-redirect-note" class="verify-note"></p>
        <div class="verify-actions">
          <button id="verify-result-login-btn" class="btn btn-primary" type="button" data-link="login">Ir a login</button>
          <button class="btn btn-soft" type="button" data-link="resend">Reenviar verificación</button>
        </div>
      </div>
    </div>
    `,
    () => {
      const note = byId("verify-result-redirect-note");
      const loginBtn = byId("verify-result-login-btn");

      if (!note || !loginBtn) return;

      if (!isSuccess) {
        note.textContent = "Puedes intentar de nuevo desde 'Reenviar verificación' o volver al login.";
        return;
      }

      let secondsLeft = 3;
      const updateNote = () => {
        note.innerHTML = `Redirigiendo automáticamente en <strong>${secondsLeft}</strong> segundo(s)... Si no ocurre, usa <a href="#/login">este enlace</a>.`;
      };

      updateNote();
      const tick = () => {
        secondsLeft -= 1;
        if (secondsLeft <= 0) {
          location.hash = "#/login";
          return;
        }
        updateNote();
        verifyRedirectTimer = setTimeout(tick, 1000);
      };

      verifyRedirectTimer = setTimeout(tick, 1000);
      loginBtn.addEventListener("click", () => {
        if (verifyRedirectTimer) {
          clearTimeout(verifyRedirectTimer);
          verifyRedirectTimer = null;
        }
      });
    }
  );
}

function toggleSpecialLayout(path) {
  document.body.classList.toggle("verify-result-mode", path === "verify-result");
}

function renderResend() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Reenviar email de verificacion</h3>
          <p>Genera un nuevo token si el anterior expiro o no llego.</p>
        </div>
      </div>
      <form id="resend-form" class="card">
        <div class="field">
          <label for="resend-email">Email</label>
          <input id="resend-email" type="email" placeholder="correo@ejemplo.com" required />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Reenviar</button>
          <span class="route-link" data-link="verify">Tengo el token y quiero verificar</span>
        </div>
      </form>
      <div id="resend-result" class="result-box hidden"></div>
    </section>
    `,
    () => {
      const form = byId("resend-form");
      const resultBox = byId("resend-result");
      if (state.lastEmail) byId("resend-email").value = state.lastEmail;

      form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const email = byId("resend-email").value.trim();
        state.lastEmail = email;
        localStorage.setItem(STORAGE_KEYS.lastEmail, email);

        const result = await apiCall("POST", "/auth/resend-verification", { email });
        if (!result.ok) return;

        const devToken = extractTokenFromUrl(result.data?.verificar_url_dev);
        if (devToken) state.prefillVerifyToken = devToken;

        let html = `<strong>${escapeHtml(result.data?.message || "Solicitud enviada.")}</strong>`;
        if (devToken) {
          html += `
            <p class="hint">Modo Development: token de verificacion disponible.</p>
            <div class="form-actions">
              <button type="button" class="btn btn-soft" id="resend-go-verify-btn">Abrir Verify con token</button>
            </div>
          `;
        }
        resultBox.innerHTML = html;
        resultBox.classList.remove("hidden");

        const verifyBtn = byId("resend-go-verify-btn");
        if (verifyBtn) {
          verifyBtn.addEventListener("click", () => {
            location.hash = "#/verify";
          });
        }
      });
    }
  );
}

function renderHealth() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Estado del backend</h3>
          <p>Consulta GET /health para revisar dependencias.</p>
        </div>
      </div>
      <div class="card">
        <div class="form-actions">
          <button id="health-btn" class="btn btn-primary" type="button">Consultar health</button>
        </div>
        <div id="health-result" class="result-box hidden"></div>
      </div>
    </section>
    `,
    () => {
      byId("health-btn").addEventListener("click", async () => {
        const result = await apiCall("GET", "/health");
        if (!result.ok) return;

        const checks = Array.isArray(result.data?.checks) ? result.data.checks : [];
        const lines = checks.map((c) => {
          const desc = c.description ? ` (${c.description})` : "";
          return `<li><strong>${escapeHtml(c.name)}:</strong> ${escapeHtml(c.status)}${escapeHtml(desc)}</li>`;
        });

        const html = `
          <p><strong>Estado general:</strong> ${escapeHtml(result.data?.status || "Desconocido")}</p>
          <ul>${lines.join("")}</ul>
        `;

        const box = byId("health-result");
        box.innerHTML = html;
        box.classList.remove("hidden");
      });
    }
  );
}
function renderAppHome() {
  const claims = decodeJwtPayload(state.accessToken);
  mountView(
    `
    <section class="screen screen-wide">
      <div class="screen-head">
        <div>
          <h3>Panel autenticado</h3>
          <p>Estas dentro de la seccion privada del sistema.</p>
        </div>
      </div>
      <div class="tokens-grid">
        <div class="card">
          <h4>Resumen JWT</h4>
          <p class="hint">Claims principales de la sesion actual.</p>
          <div class="mono">${escapeHtml(JSON.stringify(claims || {}, null, 2))}</div>
        </div>
        <div class="card">
          <h4>Acciones rapidas</h4>
          <div class="form-actions">
            <button class="btn btn-soft" data-link="app/me" type="button">Ver perfil</button>
            <button class="btn btn-soft" data-link="app/sessions" type="button">Gestionar sesiones</button>
            <button class="btn btn-soft" data-link="app/tokens" type="button">Renovar token</button>
          </div>
        </div>
      </div>
      <div class="card">
        <h4>Token actual</h4>
        <p class="mono">${escapeHtml(state.accessToken || "-")}</p>
      </div>
    </section>
    `,
    () => {}
  );
}

function renderAppMe() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Mi perfil</h3>
          <p>Consulta de datos del usuario autenticado.</p>
        </div>
      </div>
      <div class="card">
        <div class="form-actions">
          <button id="load-me-btn" class="btn btn-primary" type="button">Cargar perfil</button>
        </div>
        <div id="me-result" class="result-box hidden"></div>
      </div>
    </section>
    `,
    () => {
      const resultBox = byId("me-result");
      byId("load-me-btn").addEventListener("click", async () => {
        const result = await apiCall("GET", "/auth/me", null, true);
        if (!result.ok) return;
        resultBox.innerHTML = `<pre class="mono">${escapeHtml(JSON.stringify(result.data, null, 2))}</pre>`;
        resultBox.classList.remove("hidden");
      });

      byId("load-me-btn").click();
    }
  );
}

function renderAppSessions() {
  mountView(
    `
    <section class="screen screen-wide">
      <div class="screen-head">
        <div>
          <h3>Sesiones activas</h3>
          <p>Listado y revocacion por dispositivo.</p>
        </div>
      </div>
      <div class="card">
        <div class="form-actions">
          <button id="load-sessions-btn" class="btn btn-primary" type="button">Cargar sesiones</button>
        </div>
        <div id="sessions-wrap"></div>
      </div>
    </section>
    `,
    () => {
      const wrap = byId("sessions-wrap");
      const loadButton = byId("load-sessions-btn");

      async function loadSessions() {
        const result = await apiCall("GET", "/auth/sessions", null, true);
        if (!result.ok) return;

        const sessions = Array.isArray(result.data?.sesiones) ? result.data.sesiones : [];
        if (sessions.length === 0) {
          wrap.innerHTML = `<p class="hint">No hay sesiones activas.</p>`;
          return;
        }

        const rows = sessions
          .map((session) => {
            const id = escapeHtml(String(session.idSesion ?? ""));
            const ua = escapeHtml(session.userAgent || "-");
            const ip = escapeHtml(session.ipOrigen || "-");
            const created = escapeHtml(formatDate(session.creacion));
            const expires = escapeHtml(formatDate(session.expiraEn));
            return `
              <tr>
                <td>${id}</td>
                <td>${ua}</td>
                <td>${ip}</td>
                <td>${created}</td>
                <td>${expires}</td>
                <td><button class="btn btn-danger revoke-btn" data-session-id="${id}" type="button">Revocar</button></td>
              </tr>
            `;
          })
          .join("");

        wrap.innerHTML = `
          <table class="table">
            <thead>
              <tr>
                <th>ID</th>
                <th>User Agent</th>
                <th>IP</th>
                <th>Creada</th>
                <th>Expira</th>
                <th>Accion</th>
              </tr>
            </thead>
            <tbody>${rows}</tbody>
          </table>
        `;

        wrap.querySelectorAll(".revoke-btn").forEach((button) => {
          button.addEventListener("click", async () => {
            const sessionId = button.dataset.sessionId;
            if (!sessionId) return;
            const revokeResult = await apiCall("POST", `/auth/sessions/revoke/${sessionId}`, null, true);
            if (!revokeResult.ok) return;
            showFlash("Sesion revocada.", "ok");
            await loadSessions();
          });
        });
      }

      loadButton.addEventListener("click", loadSessions);
      loadButton.click();
    }
  );
}

function renderAppSecurity() {
  mountView(
    `
    <section class="screen">
      <div class="screen-head">
        <div>
          <h3>Cambio de contrasena</h3>
          <p>Este flujo invalida todas las sesiones activas.</p>
        </div>
      </div>
      <form id="change-password-form" class="card">
        <div class="field">
          <label for="current-password">Contrasena actual</label>
          <input id="current-password" type="password" required />
        </div>
        <div class="field">
          <label for="new-password">Nueva contrasena</label>
          <input id="new-password" type="password" required />
        </div>
        <div class="field">
          <label for="new-password-confirm">Confirmar nueva contrasena</label>
          <input id="new-password-confirm" type="password" required />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">Actualizar contrasena</button>
        </div>
      </form>
      <div id="change-password-result" class="result-box hidden"></div>
    </section>
    `,
    () => {
      const form = byId("change-password-form");
      const resultBox = byId("change-password-result");
      form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const passwordNueva = byId("new-password").value;
        const passwordNuevaConfirmacion = byId("new-password-confirm").value;
        if (passwordNueva !== passwordNuevaConfirmacion) {
          showFlash("Las contrasenas nuevas no coinciden.", "error");
          return;
        }

        const payload = {
          passwordActual: byId("current-password").value,
          passwordNueva,
          passwordNuevaConfirmacion,
        };

        const result = await apiCall("POST", "/auth/change-password", payload, true);
        if (!result.ok) return;

        let html = `<strong>${escapeHtml(result.data?.message || "Solicitud enviada.")}</strong>`;
        const confirmDevUrl = result.data?.confirmar_cambio_url_dev;
        if (confirmDevUrl) {
          html += `
            <p class="hint">Modo Development: enlace de confirmacion disponible.</p>
            <div class="form-actions">
              <a class="btn btn-soft" href="${escapeHtml(confirmDevUrl)}" target="_blank" rel="noopener noreferrer">Confirmar cambio ahora</a>
            </div>
          `;
        }
        resultBox.innerHTML = html;
        resultBox.classList.remove("hidden");
        showFlash("Revisa tu correo para confirmar el cambio de contrasena.", "ok");
      });
    }
  );
}

function renderAppTokens() {
  const claims = decodeJwtPayload(state.accessToken);
  mountView(
    `
    <section class="screen screen-wide">
      <div class="screen-head">
        <div>
          <h3>Tokens y cierre de sesion</h3>
          <p>Rotacion de refresh token y control de salida.</p>
        </div>
      </div>
      <div class="card">
        <div class="field">
          <label for="refresh-token-input">Refresh token</label>
          <textarea id="refresh-token-input" placeholder="Refresh token vigente"></textarea>
        </div>
        <div class="form-actions">
          <button id="refresh-btn" class="btn btn-primary" type="button">POST /auth/refresh-token</button>
          <button id="logout-btn" class="btn btn-danger" type="button">POST /auth/logout</button>
          <button id="logout-all-btn" class="btn btn-danger" type="button">POST /auth/logout-all</button>
        </div>
      </div>
      <div class="card">
        <h4>Claims actuales</h4>
        <pre class="mono">${escapeHtml(JSON.stringify(claims || {}, null, 2))}</pre>
      </div>
    </section>
    `,
    () => {
      const refreshInput = byId("refresh-token-input");
      refreshInput.value = state.refreshToken;

      byId("refresh-btn").addEventListener("click", async () => {
        const refreshToken = refreshInput.value.trim();
        if (!refreshToken) {
          showFlash("Debes ingresar un refresh token.", "error");
          return;
        }

        const result = await apiCall("POST", "/auth/refresh-token", { refreshToken });
        if (!result.ok) return;

        const tokens = getTokensFromPayload(result.data);
        if (!tokens) {
          showFlash("Refresh respondio sin estructura de tokens.", "error");
          return;
        }

        setSession(tokens.accessToken, tokens.refreshToken);
        refreshInput.value = state.refreshToken;
        showFlash("Tokens renovados.", "ok");
      });

      byId("logout-btn").addEventListener("click", async () => {
        const result = await apiCall("POST", "/auth/logout", null, true);
        if (!result.ok) return;

        clearSession();
        showFlash("Sesion actual cerrada.", "ok");
        location.hash = "#/login";
      });

      byId("logout-all-btn").addEventListener("click", async () => {
        const result = await apiCall("POST", "/auth/logout-all", null, true);
        if (!result.ok) return;

        clearSession();
        showFlash("Todas las sesiones cerradas.", "ok");
        location.hash = "#/login";
      });
    }
  );
}

function mountView(html, onMount) {
  dom.view.innerHTML = html;
  bindRouteLinks();
  if (typeof onMount === "function") onMount();
}

function bindRouteLinks() {
  dom.view.querySelectorAll("[data-link]").forEach((element) => {
    element.addEventListener("click", () => {
      const target = element.dataset.link;
      if (!target) return;
      location.hash = "#/" + target;
    });
  });
}

function setActiveMenu(path) {
  document.querySelectorAll(".menu-link").forEach((button) => {
    const isActive = button.dataset.route === path;
    button.classList.toggle("active", isActive);
  });
}

function updateSessionUi() {
  const claims = decodeJwtPayload(state.accessToken);
  const hasSession = Boolean(state.accessToken);

  dom.authPill.textContent = hasSession ? "Sesion activa" : "Sin sesion";
  dom.authPill.className = "pill " + (hasSession ? "pill-ok" : "pill-muted");
  dom.userPill.textContent = hasSession
    ? `Usuario: ${claims?.email || claims?.id || "desconocido"}`
    : "Usuario: -";

  document.querySelectorAll(".menu-private").forEach((button) => {
    button.classList.toggle("disabled", !hasSession);
  });
}

function saveApiBaseUrl() {
  const normalized = normalizeBaseUrl(dom.apiBaseUrl.value);
  if (!normalized) {
    showFlash("Ingresa una API Base URL valida.", "error");
    return;
  }

  state.apiBaseUrl = normalized;
  localStorage.setItem(STORAGE_KEYS.apiBaseUrl, normalized);
  dom.apiBaseUrl.value = normalized;
  showFlash("API Base URL guardada.", "ok");
}

function setSession(accessToken, refreshToken) {
  state.accessToken = accessToken || "";
  state.refreshToken = refreshToken || "";
  localStorage.setItem(STORAGE_KEYS.accessToken, state.accessToken);
  localStorage.setItem(STORAGE_KEYS.refreshToken, state.refreshToken);
  updateSessionUi();
}

function clearSession() {
  state.accessToken = "";
  state.refreshToken = "";
  localStorage.removeItem(STORAGE_KEYS.accessToken);
  localStorage.removeItem(STORAGE_KEYS.refreshToken);
  updateSessionUi();
}

function getHashState() {
  const hash = location.hash.replace(/^#\/?/, "");
  const fallback = state.accessToken ? "app/home" : "login";
  if (!hash) return { path: fallback, params: new URLSearchParams() };

  const [path, queryString] = hash.split("?");
  return {
    path,
    params: new URLSearchParams(queryString || ""),
  };
}

async function submitGoogleToken(idToken) {
  if (!idToken) {
    showFlash("Debes proveer un ID Token de Google.", "error");
    return;
  }

  const result = await apiCall("POST", "/auth/google", { idToken });
  if (!result.ok) return;

  const tokens = getTokensFromPayload(result.data);
  if (!tokens) {
    showFlash("Login Google respondio sin tokens esperados.", "error");
    return;
  }

  setSession(tokens.accessToken, tokens.refreshToken);
  showFlash("Login con Google exitoso.", "ok");
  location.hash = "#/app/home";
}

function initGoogleSignIn(clientId) {
  const host = byId("google-button-host");
  if (!host) return;

  if (!window.google || !window.google.accounts || !window.google.accounts.id) {
    showFlash("No se pudo cargar la libreria de Google. Verifica internet.", "error");
    return;
  }

  host.innerHTML = "";
  window.google.accounts.id.initialize({
    client_id: clientId,
    callback: async (response) => {
      const tokenInput = byId("google-id-token");
      if (tokenInput) tokenInput.value = response.credential || "";
      await submitGoogleToken(response.credential || "");
    },
  });

  window.google.accounts.id.renderButton(host, {
    theme: "outline",
    size: "large",
    text: "continue_with",
    width: 300,
    shape: "pill",
  });

  showFlash("Google Sign-In inicializado.", "ok");
}
async function apiCall(method, path, body = null, auth = false) {
  const url = `${normalizeBaseUrl(state.apiBaseUrl)}${path}`;
  const headers = {};
  if (body !== null) headers["Content-Type"] = "application/json";
  if (auth && state.accessToken) headers.Authorization = `Bearer ${state.accessToken}`;

  const startedAt = performance.now();

  try {
    const response = await fetch(url, {
      method,
      headers,
      body: body !== null ? JSON.stringify(body) : undefined,
    });

    const duration = Math.round(performance.now() - startedAt);
    const payload = await readResponsePayload(response);

    writeConsole({
      method,
      path,
      status: response.status,
      duration,
      payload,
    });

    if (!response.ok) {
      const message = payload?.message || `HTTP ${response.status}`;
      showFlash(message, "error");
      return { ok: false, status: response.status, data: payload };
    }

    return { ok: true, status: response.status, data: payload };
  } catch (error) {
    const duration = Math.round(performance.now() - startedAt);
    const payload = {
      message: "No se pudo conectar con el backend.",
      detail: String(error?.message || error),
      hint: "Verifica que la API este corriendo y que CORS permita este origen.",
    };

    writeConsole({
      method,
      path,
      status: 0,
      duration,
      payload,
    });

    showFlash(payload.message, "error");
    return { ok: false, status: 0, data: payload };
  }
}

async function readResponsePayload(response) {
  const contentType = response.headers.get("content-type") || "";
  if (contentType.includes("application/json")) {
    return response.json();
  }
  const text = await response.text();
  return { raw: text };
}

function writeConsole({ method, path, status, duration, payload }) {
  const stamp = new Date().toLocaleTimeString();
  const tag = status === 0 ? "ERROR" : `HTTP ${status}`;
  dom.httpMeta.textContent = `${stamp} | ${method} ${path} | ${tag} | ${duration} ms`;
  dom.httpBody.textContent = JSON.stringify(payload, null, 2);
}

function getTokensFromPayload(payload) {
  const tokenContainer = payload?.tokens ?? payload;
  if (!tokenContainer) return null;

  if (
    typeof tokenContainer.accessToken === "string" &&
    typeof tokenContainer.refreshToken === "string"
  ) {
    return {
      accessToken: tokenContainer.accessToken,
      refreshToken: tokenContainer.refreshToken,
    };
  }

  return null;
}

function decodeJwtPayload(token) {
  if (!token || typeof token !== "string") return null;
  const parts = token.split(".");
  if (parts.length !== 3) return null;

  try {
    const base64Url = parts[1];
    const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64 + "=".repeat((4 - (base64.length % 4)) % 4);
    const decoded = atob(padded);
    return JSON.parse(decoded);
  } catch {
    return null;
  }
}

function extractTokenFromUrl(url) {
  if (!url || typeof url !== "string") return "";
  const clean = url.trim();
  if (!clean) return "";
  const parts = clean.split("/");
  const token = parts[parts.length - 1] || "";
  return decodeURIComponent(token);
}

function showFlash(message, type = "ok") {
  dom.flash.textContent = message;
  dom.flash.classList.remove("hidden");
  dom.flash.classList.toggle("error", type === "error");

  if (flashTimeout) clearTimeout(flashTimeout);
  flashTimeout = setTimeout(() => {
    dom.flash.classList.add("hidden");
  }, 4500);
}

async function copyToClipboard(text) {
  if (!navigator.clipboard) return;
  await navigator.clipboard.writeText(text || "");
}

function normalizeBaseUrl(value) {
  const raw = String(value || "").trim();
  if (!raw) return "";
  return raw.replace(/\/+$/, "");
}

function formatDate(value) {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString("es-CL");
  } catch {
    return String(value);
  }
}

function escapeHtml(value) {
  const map = {
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  };
  return String(value ?? "").replace(/[&<>"']/g, (char) => map[char]);
}

function byId(id) {
  return document.getElementById(id);
}
