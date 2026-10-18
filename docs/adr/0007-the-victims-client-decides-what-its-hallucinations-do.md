# The Victim's client decides what its Hallucinations do, death included

Elsewhere the Master decides and every client applies, but a Hallucination exists only on the Victim's client (ADR-0002), so that client alone decides what the Hallucination does: the Lunge that kills the Victim and the Mimic it speaks then. The death goes through the game's own path for the local player, which the Victim's client owns; the Master's only part is the rule, an option it sets and shares that lets Hallucinations kill.

Moving the decision to the Master would mean putting Hallucinations on the network, which ADR-0002 rules out. The cost is trust: a modified client can ignore its own death, which a mod played between friends accepts.
