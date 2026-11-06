#!/usr/bin/env python3
"""Yuklama testi uchun soxta SSO (faqat sinov!): OIDC discovery + JWKS beradi va foydalanuvchilarga access token yozadi.

IdM API tokenni haqiqiy SSO kabi tekshiradi (imzo, iss, aud, muddat, "idm" scope) — API kodi o'zgarmaydi.
SimpleIdServer'ning o'zi bu testda o'lchanmaydi (u alohida sinaladi).

Ishlatish:
  mock-sso.py --dir WORK --port 7443 --tokens tokens.json --users login0,login1 ...   (tokenlarni yozadi va serverni ishga tushiradi)
Faqat Python standart kutubxonasi va openssl buyrug'i kerak.
"""
import argparse, base64, hashlib, http.server, json, os, re, ssl, subprocess, time

def b64u(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()

def ensure_keys(work: str):
    key, cert = os.path.join(work, "sso-key.pem"), os.path.join(work, "sso-cert.pem")
    if not os.path.exists(key):
        subprocess.run(["openssl", "genrsa", "-out", key, "2048"], check=True, capture_output=True)
        subprocess.run(["openssl", "req", "-x509", "-key", key, "-out", cert, "-days", "30", "-subj", "/CN=localhost",
                        "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1"], check=True, capture_output=True)
    text = subprocess.run(["openssl", "rsa", "-in", key, "-noout", "-text"], check=True, capture_output=True, text=True).stdout
    def field(name):
        m = re.search(name + r":\s*\n((?:\s+[0-9a-f:]+\n)+)", text)
        return int(re.sub(r"[\s:]", "", m.group(1)), 16)
    return key, cert, field("modulus"), field("privateExponent")

def sign_rs256(n: int, d: int, message: bytes) -> bytes:
    # RSASSA-PKCS1-v1_5 + SHA-256
    digest_info = bytes.fromhex("3031300d060960864801650304020105000420") + hashlib.sha256(message).digest()
    k = (n.bit_length() + 7) // 8
    em = b"\x00\x01" + b"\xff" * (k - len(digest_info) - 3) + b"\x00" + digest_info
    return pow(int.from_bytes(em, "big"), d, n).to_bytes(k, "big")

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--dir", required=True)
    p.add_argument("--port", type=int, default=7443)
    p.add_argument("--tokens", required=True, help="JSON fayl: {login: token}")
    p.add_argument("--users", required=True, help="vergul bilan loginlar")
    p.add_argument("--ttl", type=int, default=4 * 3600)
    a = p.parse_args()
    os.makedirs(a.dir, exist_ok=True)
    key, cert, n, d = ensure_keys(a.dir)
    issuer = f"https://localhost:{a.port}/master"
    kid = hashlib.sha256(n.to_bytes(256, "big")).hexdigest()[:16]
    jwks = {"keys": [{"kty": "RSA", "use": "sig", "alg": "RS256", "kid": kid,
                      "n": b64u(n.to_bytes((n.bit_length() + 7) // 8, "big")), "e": "AQAB"}]}

    now = int(time.time())
    header = b64u(json.dumps({"alg": "RS256", "typ": "JWT", "kid": kid}).encode())
    tokens = {}
    for login in [u for u in a.users.split(",") if u]:
        payload = b64u(json.dumps({"iss": issuer, "aud": "idm-portal", "sub": login, "name": login, "scope": "openid profile idm",
                                   "amr": ["pwd", "otp"], "iat": now, "nbf": now, "exp": now + a.ttl}).encode())
        tokens[login] = f"{header}.{payload}." + b64u(sign_rs256(n, d, f"{header}.{payload}".encode()))
    with open(a.tokens, "w") as f:
        json.dump(tokens, f)

    discovery = {"issuer": issuer, "jwks_uri": f"{issuer}/jwks", "authorization_endpoint": f"{issuer}/authorize",
                 "token_endpoint": f"{issuer}/token", "id_token_signing_alg_values_supported": ["RS256"]}

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            body = {"/master/.well-known/openid-configuration": discovery, "/master/jwks": jwks}.get(self.path)
            if body is None:
                self.send_response(404); self.end_headers(); return
            data = json.dumps(body).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        def log_message(self, *args):
            pass

    server = http.server.ThreadingHTTPServer(("127.0.0.1", a.port), Handler)
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(cert, key)
    server.socket = ctx.wrap_socket(server.socket, server_side=True)
    print(f"mock-sso: {issuer} ({len(tokens)} token)", flush=True)
    server.serve_forever()

if __name__ == "__main__":
    main()
