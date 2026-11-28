"""Write private per-run HTTPS, service signing and MFA configuration for the isolated Compose stack."""
import base64
import json
from pathlib import Path
import secrets
import subprocess
import sys

scratch = Path(sys.argv[1])


def openssl(*args):
    return subprocess.check_output(["openssl", *args], cwd=scratch, stderr=subprocess.DEVNULL)


def signing_key(name):
    key = name + ".signing.key"
    openssl("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", key)
    return ((scratch / key).read_text(), openssl("pkey", "-in", key, "-pubout").decode())


def secure_service(name, peer, local_keys, peer_keys, assembly):
    openssl("req", "-newkey", "rsa:2048", "-nodes", "-keyout", name + ".key",
            "-subj", "/CN=" + name, "-out", name + ".csr")
    extensions = scratch / (name + ".ext")
    extensions.write_text("subjectAltName=DNS:" + name + ",DNS:localhost\nextendedKeyUsage=serverAuth\n"
                          "authorityKeyIdentifier=keyid:always\nsubjectKeyIdentifier=hash\n"
                          "basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n")
    cert = openssl("x509", "-req", "-in", name + ".csr", "-CA", "ca.crt", "-CAkey", "ca.key",
                   "-CAcreateserial", "-days", "2", "-extfile", extensions.name)
    env = {
        "ASPNETCORE_URLS": "http://+:8080;https://+:8443",
        "ASPNETCORE_Kestrel__Certificates__Default__Path": "/tmp/fixture.crt",
        "ASPNETCORE_Kestrel__Certificates__Default__KeyPath": "/tmp/fixture.key",
        "SSL_CERT_FILE": "/tmp/fixture-ca.crt",
        "FIXTURE_CERT": cert.decode(),
        "FIXTURE_CA": (scratch / "ca.crt").read_text(),
        "FIXTURE_KEY": (scratch / (name + ".key")).read_text(),
        "EntitlementDelivery__Enabled": "true",
        "EntitlementDelivery__PeerOrigin": "https://" + peer + ":8443",
        "EntitlementDelivery__LocalKeyId": name.replace("-", ""),
        "EntitlementDelivery__LocalPrivateKeyPem": local_keys[0],
        "EntitlementDelivery__PublicKeys__" + peer.replace("-", ""): peer_keys[1],
    }
    start = """umask 077
printf '%s' "$$FIXTURE_CERT" > /tmp/fixture.crt
printf '%s' "$$FIXTURE_KEY" > /tmp/fixture.key
printf '%s' "$$FIXTURE_CA" > /tmp/fixture-ca.crt
exec dotnet """ + assembly + ' "$$@"'
    return {"environment": env, "entrypoint": ["/bin/sh", "-ec", start, "fixture"]}


openssl("req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2", "-subj", "/CN=P0702 local CA",
        "-keyout", "ca.key", "-out", "ca.crt", "-addext", "basicConstraints=critical,CA:TRUE",
        "-addext", "keyUsage=critical,keyCertSign,cRLSign", "-addext", "subjectKeyIdentifier=hash")
merchant = signing_key("merchant")
licensing = signing_key("licensing")
delegation = signing_key("delegation")
api = secure_service("licensing-api", "backend", licensing, merchant, "Intelibill.Licensing.Api.dll")
api["environment"].update({
    "AdminMfa__ActiveKeyId": "local",
    "AdminMfa__Keys__local": base64.b64encode(secrets.token_bytes(32)).decode(),
    "AdminSessions__SigningKey": base64.b64encode(secrets.token_bytes(32)).decode(),
    "MerchantBillingVerification__PublicKeys__p0702": delegation[1],
    "Notifications__IntervalMilliseconds": "500",
})
backend = secure_service("backend", "licensing-api", merchant, licensing, "Intelibill.Api.dll")
backend["ports"] = ["127.0.0.1::8443"]
backend["environment"].update({
    "MerchantBillingDelegation__LicensingOrigin": "https://licensing-api:8443",
    "MerchantBillingDelegation__LocalKeyId": "p0702",
    "MerchantBillingDelegation__LocalPrivateKeyPem": delegation[0],
})
(scratch / "override.json").write_text(json.dumps({"services": {"backend": backend, "licensing-api": api}}))
