-- =========================================================
-- TABLA: USUARIOS
-- Almacena las cuentas de usuario del sistema de autenticación.
-- proveedor_login: 'LOCAL' = registro con email/password,
--                 'GOOGLE' = login con Google (futuro),
--                 'MIXTO' = ambos métodos vinculados.
-- =========================================================
CREATE TABLE USUARIOS (
    id_usuario       INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email            VARCHAR(150)  NOT NULL UNIQUE,
    nombre           VARCHAR(100),
    foto_url         VARCHAR(300),
    password_hash    VARCHAR(200),
    proveedor_login  VARCHAR(30)   NOT NULL DEFAULT 'LOCAL' CHECK (proveedor_login IN ('LOCAL', 'GOOGLE', 'MIXTO')),
    google_sub       VARCHAR(60)   UNIQUE,
    email_verificado SMALLINT      DEFAULT 0 NOT NULL CHECK (email_verificado IN (0, 1)),
    creacion         TIMESTAMPTZ   DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado           SMALLINT      DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

CREATE INDEX IDX_USUARIOS_PROVEEDOR_LOGIN ON USUARIOS (proveedor_login);

-- =========================================================
-- TABLA: SESIONES_USUARIOS
-- Registra cada sesión activa. El refresh token se almacena
-- como hash SHA-256 (nunca en texto plano).
-- ip_origen: VARCHAR(45) para soportar IPv6 (máx 45 chars).
-- =========================================================
CREATE TABLE SESIONES_USUARIOS (
    id_sesion     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario    INTEGER      NOT NULL REFERENCES USUARIOS (id_usuario),
    token_refresh VARCHAR(300) NOT NULL UNIQUE,
    expira_en     TIMESTAMPTZ  NOT NULL,
    user_agent    VARCHAR(300),
    ip_origen     VARCHAR(45),
    creacion      TIMESTAMPTZ  DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado        SMALLINT     DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

CREATE INDEX IDX_SESIONES_USUARIOS_ID_USUARIO ON SESIONES_USUARIOS (id_usuario);
CREATE INDEX IDX_SESIONES_TOKEN_REFRESH       ON SESIONES_USUARIOS (token_refresh);

-- =========================================================
-- TABLA: VERIFICACION_EMAIL
-- Tokens temporales enviados al registrarse (TTL: 24 horas).
-- =========================================================
CREATE TABLE VERIFICACION_EMAIL (
    id_verificacion INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario      INTEGER      NOT NULL REFERENCES USUARIOS (id_usuario),
    token           VARCHAR(200) NOT NULL,
    expira_en       TIMESTAMPTZ  NOT NULL,
    creacion        TIMESTAMPTZ  DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado          SMALLINT     DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

CREATE INDEX IDX_VERIFEMAIL_ID_USUARIO ON VERIFICACION_EMAIL (id_usuario);
CREATE INDEX IDX_VERIFEMAIL_TOKEN      ON VERIFICACION_EMAIL (token);

-- =========================================================
-- TABLA: RESET_PASSWORD
-- Tokens temporales para recuperación de contraseña (TTL: 1h).
-- =========================================================
CREATE TABLE RESET_PASSWORD (
    id_reset   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario INTEGER      NOT NULL REFERENCES USUARIOS (id_usuario),
    token      VARCHAR(200) NOT NULL,
    expira_en  TIMESTAMPTZ  NOT NULL,
    creacion   TIMESTAMPTZ  DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado     SMALLINT     DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

CREATE INDEX IDX_RESETPASS_ID_USUARIO ON RESET_PASSWORD (id_usuario);
CREATE INDEX IDX_RESETPASS_TOKEN      ON RESET_PASSWORD (token);

-- =========================================================
-- TABLA: INTENTOS_LOGIN
-- Registra intentos fallidos de login para implementar
-- account lockout temporal. Se limpia periódicamente.
-- bloqueado_hasta: NULL = no bloqueado, fecha = bloqueado hasta esa hora.
-- =========================================================
CREATE TABLE INTENTOS_LOGIN (
    id_intento      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email           VARCHAR(150) NOT NULL,
    ip_origen       VARCHAR(45)  NOT NULL,
    intentos        INTEGER      DEFAULT 1 NOT NULL,
    ultimo_intento  TIMESTAMPTZ  DEFAULT CURRENT_TIMESTAMP NOT NULL,
    bloqueado_hasta TIMESTAMPTZ
);

CREATE INDEX IDX_INTENTOS_EMAIL    ON INTENTOS_LOGIN (email);
CREATE INDEX IDX_INTENTOS_IP       ON INTENTOS_LOGIN (ip_origen);
