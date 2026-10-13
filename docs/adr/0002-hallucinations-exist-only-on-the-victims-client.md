# Hallucinations exist only on the Victim's client

Hallucinations are plain local objects spawned on the Victim's machine, never network objects: no other player can see them, and nothing about their movement or Stalking is synchronised. The Master only broadcasts Mimic commands; the Victim's client resolves which local Hallucination speaks.

This keeps the illusion private to the Victim at zero network cost, but it means any Hallucination behaviour that other players should perceive has to be designed as a separate Effect (as Dilated Pupils is), never by networking the Hallucination itself.
