-- =========================================================
-- TABLA: USUARIOS
-- =========================================================
CREATE TABLE USUARIOS (
    id_usuario        SERIAL         PRIMARY KEY,
    email             VARCHAR(150)   NOT NULL UNIQUE,
    nombre            VARCHAR(100),
    foto_url          VARCHAR(300),
    password_hash     VARCHAR(200),
    proveedor_login   VARCHAR(30)    CHECK (proveedor_login IN ('LOCAL', 'GOOGLE', 'MIXTO')),
    google_sub        VARCHAR(60)    UNIQUE,
    email_verificado  SMALLINT       DEFAULT 0 NOT NULL CHECK (email_verificado IN (0, 1)),
    propietario       INTEGER        NOT NULL,
    creacion          TIMESTAMP      DEFAULT CURRENT_TIMESTAMP NOT NULL,
    usuario           INTEGER,
    actualizacion     TIMESTAMP,
    estado            SMALLINT       DEFAULT 1 NOT NULL CHECK (estado IN (0,1))
);

-- Índices adicionales
CREATE INDEX IDX_USUARIOS_PROVEEDOR_LOGIN ON USUARIOS (proveedor_login);

-- =========================================================
-- TABLA: SESIONES_USUARIOS
-- =========================================================
CREATE TABLE SESIONES_USUARIOS (
    id_sesion     SERIAL        PRIMARY KEY,
    id_usuario    INTEGER       NOT NULL REFERENCES USUARIOS (id_usuario),
    token_refresh VARCHAR(300)  NOT NULL,
    expira_en     TIMESTAMP     NOT NULL,
    user_agent    VARCHAR(300),
    ip_origen     VARCHAR(30),
    propietario   INTEGER       NOT NULL REFERENCES USUARIOS (id_usuario),
    creacion      TIMESTAMP     DEFAULT CURRENT_TIMESTAMP NOT NULL,
    usuario       INTEGER       REFERENCES USUARIOS (id_usuario),
    actualizacion TIMESTAMP,
    estado        SMALLINT      DEFAULT 1 NOT NULL CHECK (estado IN (0,1))
);

CREATE INDEX IDX_SESIONES_USUARIOS_ID_USUARIO ON SESIONES_USUARIOS (id_usuario);

-- =========================================================
-- TABLA: VERIFICACION_EMAIL
-- =========================================================
CREATE TABLE VERIFICACION_EMAIL (
    id_verificacion   SERIAL         PRIMARY KEY,
    id_usuario        INTEGER        NOT NULL REFERENCES USUARIOS (id_usuario),
    token             VARCHAR(200)   NOT NULL,
    expira_en         TIMESTAMP      NOT NULL,
    propietario       INTEGER        NOT NULL REFERENCES USUARIOS (id_usuario),
    creacion          TIMESTAMP      DEFAULT CURRENT_TIMESTAMP NOT NULL,
    usuario           INTEGER        REFERENCES USUARIOS (id_usuario),
    actualizacion     TIMESTAMP,
    estado            SMALLINT       DEFAULT 1 NOT NULL CHECK (estado IN (0,1))
);

CREATE INDEX IDX_VERIFEMAIL_ID_USUARIO ON VERIFICACION_EMAIL (id_usuario);
CREATE INDEX IDX_VERIFEMAIL_TOKEN ON VERIFICACION_EMAIL (token);

-- =========================================================
-- TABLA: RESET_PASSWORD
-- =========================================================
CREATE TABLE RESET_PASSWORD (
    id_reset        SERIAL         PRIMARY KEY,
    id_usuario      INTEGER        NOT NULL REFERENCES USUARIOS (id_usuario),
    token           VARCHAR(200)   NOT NULL,
    expira_en       TIMESTAMP      NOT NULL,
    propietario     INTEGER        NOT NULL REFERENCES USUARIOS (id_usuario),
    creacion        TIMESTAMP      DEFAULT CURRENT_TIMESTAMP NOT NULL,
    usuario         INTEGER        REFERENCES USUARIOS (id_usuario),
    actualizacion   TIMESTAMP,
    estado          SMALLINT       DEFAULT 1 NOT NULL CHECK (estado IN (0,1))
);

CREATE INDEX IDX_RESETPASS_ID_USUARIO ON RESET_PASSWORD (id_usuario);
CREATE INDEX IDX_RESETPASS_TOKEN ON RESET_PASSWORD (token);



-- =========================================================
-- INSERTS TABLA USUARIOS (ADMIN POR DEFECTO)
-- =========================================================
-- INSERT INTO USUARIOS (email, nombre, foto_url, password_hash, proveedor_login, google_sub, email_verificado, propietario, usuario) 
-- VALUES ('claudio.pina.cartes@gmail.com','Claudio',NULL,'$2a$12$FUnb7AqytBJZ7RxH3H7S7uJ4e1IY52Sq48RP85Qg/S7j6lU64LLge','MIXTO',NULL,1,1,NULL);

-- =========================================================
-- INSERTS TIPO_TRANSACCIONES (POR DEFECTO)
-- =========================================================
-- INSERT INTO TIPO_TRANSACCIONES (nombre, descripcion, propietario) VALUES ('INGRESO', 'Entradas de dinero', 1);
-- INSERT INTO TIPO_TRANSACCIONES (nombre, descripcion, propietario) VALUES ('GASTO', 'Salidas de dinero', 1);

-- ========================================================
-- INSERTS ORGANIZACION (PERSONAL POR DEFECTO)
-- ========================================================
-- INSERT INTO ORGANIZACION (nombre, descripcion, propietario) VALUES ('Finanzas de Claudio','Organización personal para administrar ingresos y gastos', 1);

-- ========================================================
-- INSERTS USUARIO_ORGANIZACION (ADMIN POR DEFECTO)
-- ========================================================
-- INSERT INTO USUARIO_ORGANIZACION (id_usuario, id_organizacion, rol, propietario) VALUES (1, 1, 'ADMIN_ORG', 1);

-- ========================================================
-- INSERTS CATEGORIAS (POR DEFECTO PARA ADMIN)
-- ========================================================
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Cuota', '#FF5733', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Agua DDS', '#33FF57', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Quincho', '#3357FF', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Cuota amigos', '#F1C40F', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Entel', '#8E44AD', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Agua en bidón', '#16A085', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Take a Break', '#E67E22', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'ChatGPT', '#2C3E50', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Tarjeta de crédito', '#D35400', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Ahorro mensual', '#27AE60', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Ahorro semanal', '#2980B9', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Google Drive', '#C0392B', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Spotify', '#7F8C8D', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Sobregiro', '#9B59B6', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Salud', '#34495E', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Supermercado', '#1ABC9C', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Tarjeta BIP', '#E74C3C', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Uber', '#BDC3C7', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Cursos y estudios', '#F39C12', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'BCI seguros', '#2ECC71', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Coaniquem', '#3498DB', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Sueldo', '#E91E63', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Comidas extras', '#00BCD4', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Desayuno', '#FF9800', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Entretenimiento', '#9C27B0', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Ropa y vestuario', '#3F51B5', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Accesorios', '#CDDC39', 1);
-- INSERT INTO CATEGORIAS (id_organizacion, nombre, color_hex, propietario) VALUES (1, 'Otros', '#607D8B', 1);

-- =========================================================
-- INSERTS TRANSACCIONES (POR DEFECTO PARA ADMIN)
-- =========================================================
-- INSERT INTO TRANSACCIONES (id_organizacion, id_usuario, id_tipo, id_categoria,descripcion, monto, fecha_transaccion, propietario) 
-- VALUES (1,1,1,22,'Sueldo Ejército Noviembre 2025',975000,SYSDATE,1);

-- ========================================================
-- INSERTS ARCHIVOS_TRANSACCIONES (POR DEFECTO PARA ADMIN)
-- ========================================================
-- INSERT INTO ARCHIVOS_TRANSACCIONES (id_transaccion, nombre_archivo, tipo_mime, url_archivo, propietario) 
-- VALUES (1,'Comprobante.jpg','image/jpeg','/archivos/1/Comprobante.jpg',1);
