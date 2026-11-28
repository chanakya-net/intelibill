"""Approve a synthetic standalone address and redeem its captured proof through the holder-only endpoint."""
import json
import os
from pathlib import Path
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

merchant = os.environ["P0702_MERCHANT"]
licensing = os.environ["P0702_LICENSING"]
mailpit = os.environ["P0115_MAILPIT"]
account = os.environ["P0702_ACCOUNT"]
address = "contact@p0702.test"


def request(url, values=None, headers=None):
    body = None if values is None else json.dumps(values).encode()
    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json", **(headers or {})})
    try:
        tls = ssl.create_default_context(cafile=Path(os.environ["P0115_SCRATCH"]) / "ca.crt")
        with urllib.request.urlopen(req, timeout=10, context=tls) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Local fixture request {urllib.parse.urlsplit(url).path} returned {error.code}") from None


password = (Path(os.environ["P0115_SCRATCH"]) / "password").read_text().strip()
session = json.loads(request(merchant + "/api/auth/login", {"identifier": "owner@p0702.test", "password": password}))
contact = json.loads(request(merchant + "/api/billing/accounts/" + account + "/notice-contacts", {
    "operationKey": str(uuid.uuid4()), "email": address, "locale": "bn-IN", "merchantUserId": None,
}, {"Authorization": "Bearer " + session["accessToken"]}))
assert not contact["eligible"]
for _ in range(40):
    results = json.loads(request(mailpit + "/api/v1/search?" + urllib.parse.urlencode({"query": "to:" + address})))
    if results["messages"]:
        break
    time.sleep(0.5)
else:
    raise RuntimeError("Expected one proof capture")
assert len(results["messages"]) == 1
message = json.loads(request(mailpit + "/api/v1/message/" + results["messages"][0]["ID"]))
link = next(line for line in message["Text"].splitlines() if line.startswith(licensing + "/billing-contact/verify?"))
assert "/billing/accounts" not in message["Text"]
fields = urllib.parse.parse_qs(urllib.parse.urlsplit(link).fragment)
assert fields["account"] == [account] and fields["contact"] == [contact["contactId"]]
assert b"<form>" in request(link.split("#")[0])
request(licensing + "/billing-contact/verify", {
    "accountId": account, "contactId": contact["contactId"], "operationKey": str(uuid.uuid4()),
    "verifiedEmail": address, "proofToken": fields["proof"][0], "optedIn": True, "locale": "bn-IN",
}, {"Origin": licensing})
print("Standalone contact approved, separately verified and opted in using one Bengali proof capture.")
