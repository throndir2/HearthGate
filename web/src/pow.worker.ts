// Proof-of-work solver. Finds a nonce so that SHA-256(salt + nonce) starts with `bits` zero bits.
self.onmessage = async (e: MessageEvent<{ salt: string; bits: number }>) => {
  const { salt, bits } = e.data
  const enc = new TextEncoder()
  for (let n = 0; ; n++) {
    const nonce = n.toString(36)
    const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(salt + nonce)))
    let ok = true
    for (let i = 0; i < bits; i++) {
      if (hash[i >> 3] & (0x80 >> (i & 7))) { ok = false; break }
    }
    if (ok) { self.postMessage(nonce); return }
  }
}
