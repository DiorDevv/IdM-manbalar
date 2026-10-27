-- Bank IdM: PostgreSQL rollari (eng kam huquq). DBA bir marta, superuser sifatida bajaradi:
--   psql "host=db.bank.uz dbname=idm user=postgres sslmode=verify-full" -v owner_password=... -v app_password=... -v reader_password=... -f db-roles.sql
-- Parollar sirlar saqlovchisida; bu faylda yo'q.
--
--   idm_owner  — jadvallar egasi: faqat migratsiyalar (ilova bu rol bilan ishlamaydi)
--   idm_app    — IdM API: ma'lumotlarni o'qish va yozish; audit jurnaliga faqat qo'shish (o'zgartirish, o'chirish, TRUNCATE yo'q)
--   idm_reader — hisobot va audit (masalan SIEM yoki BI): faqat o'qish
--
-- Audit triggeri (UPDATE/DELETE/TRUNCATE taqiqi) jadval egasiga tegishli: idm_app uni o'chira olmaydi.
-- API idm_app bilan ishlaganda Idm:MigrateOnStartup=false bo'lishi shart (migratsiya — idm_owner bilan alohida bosqich):
-- aks holda server ishga tushmaydi ("permission denied for schema public").

\set ON_ERROR_STOP on

DO $$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'idm_owner') THEN CREATE ROLE idm_owner LOGIN; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'idm_app') THEN CREATE ROLE idm_app LOGIN; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'idm_reader') THEN CREATE ROLE idm_reader LOGIN; END IF;
END $$;

ALTER ROLE idm_owner PASSWORD :'owner_password';
ALTER ROLE idm_app PASSWORD :'app_password';
ALTER ROLE idm_reader PASSWORD :'reader_password';
-- Ilova sessiyasi: uzoq osilib qolgan so'rovlar va tranzaksiyalar bazani band qilmasin
ALTER ROLE idm_app SET statement_timeout = '5min';
ALTER ROLE idm_app SET idle_in_transaction_session_timeout = '2min';

-- Bazaga faqat shu rollar ulanadi
REVOKE ALL ON DATABASE idm FROM PUBLIC;
GRANT CONNECT ON DATABASE idm TO idm_owner, idm_app, idm_reader;
ALTER DATABASE idm OWNER TO idm_owner;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO idm_owner;
GRANT USAGE ON SCHEMA public TO idm_app, idm_reader;

-- Mavjud jadvallar egasi — idm_owner (migratsiyalar avval boshqa rol bilan qo'llangan bo'lsa)
DO $$ DECLARE t record; BEGIN
  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
    EXECUTE format('ALTER TABLE public.%I OWNER TO idm_owner', t.tablename);
  END LOOP;
  FOR t IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' LOOP
    EXECUTE format('ALTER SEQUENCE public.%I OWNER TO idm_owner', t.sequencename);
  END LOOP;
END $$;

-- Ilova: barcha jadvallarda DML (TRUNCATE emas)
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO idm_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO idm_app;
-- Audit jurnali: faqat qo'shish va o'qish (trigger bilan birga ikki qavat himoya)
REVOKE UPDATE, DELETE, TRUNCATE ON audit_events FROM idm_app;
-- Migratsiya tarixi faqat egasiga
REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM idm_app;

GRANT SELECT ON ALL TABLES IN SCHEMA public TO idm_reader;

-- Keyingi migratsiyalar yaratadigan jadvallar uchun ham (idm_owner yaratganda)
ALTER DEFAULT PRIVILEGES FOR ROLE idm_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO idm_app;
ALTER DEFAULT PRIVILEGES FOR ROLE idm_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO idm_app;
ALTER DEFAULT PRIVILEGES FOR ROLE idm_owner IN SCHEMA public GRANT SELECT ON TABLES TO idm_reader;
