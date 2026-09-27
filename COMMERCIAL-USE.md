# Who pays for Lapsus

**Private people use Lapsus for free. Companies pay, per seat.**

The line is *who* uses it, not what they use it for and not which features they touch. Nothing is
locked, crippled or time-limited for anybody.

The operative text is [LICENSE](LICENSE) — Business Source License 1.1, whose Additional Use Grant
says all of this in legal words. This page is the plain-language version; where the two differ,
`LICENSE` governs.

## Free — no licence, no limit

- Anyone using Lapsus as a private person, on their own machine.
- **Freelancers and sole traders**, including work they are paid for.
- Students, researchers, hobbyists.
- Someone whose employer lets them use a work laptop privately, for private things.

Two of those the licence spells out on purpose, because otherwise the client or the school could
be read as the one using Lapsus: a freelancer works on their own behalf, not their client's, and a
student's own coursework is their own — whoever owns the laptop.

If you are not a company, you are done reading. Nothing in the app will ever ask you for money.

## Paid — one seat per person

- Employees using Lapsus at a company, institution or other organisation.
- Any machine a company owns or administers, when it is used for that company's work.
- Public bodies, schools, universities and charities, as part of their operations.

The company buys the licence, not the employee.

## Trying it at work

Thirty days from the first launch on a company machine. The reminder counts them down, then
changes its wording. Nothing stops working when the period ends — Lapsus keeps correcting exactly as
before, and the reminder keeps appearing.

## How the app knows

At every launch Lapsus checks whether this computer is administered by an organisation:

| Windows | macOS |
|---|---|
| Joined to an Active Directory domain | Enrolled with an MDM server (including DEP/ABM) |
| Joined to Entra ID (Azure AD) | Bound to an Active Directory domain |
| Enrolled with a real MDM server | |

All three mean a company runs the machine. Nothing weaker counts — in particular the **Windows
edition is not checked**, because Enterprise LTSC and Education turn up on plenty of home machines
(students get Education keys for free) and nagging those people forever would be wrong.

This check runs entirely on your computer: nothing is sent, nothing is collected, and no feature is
switched off whatever it finds. On a free install Lapsus makes no network requests at all.

**Once you buy a license, that changes in exactly one way.** A license is tied to one computer, so a
key cannot be passed around an office. Entering a key sends the key and a machine fingerprint —
`SHA-256(salt + platform id)`, truncated to 22 characters — to the license server, which returns an
activation token good for that computer and 35 days. The token is checked offline at every launch
and renewed silently about a week before it lapses, so a licensed machine contacts us roughly once a
month and never at any other time.

The fingerprint cannot be reversed into a serial number and is salted per product, so it correlates
with nothing outside Lapsus. What you type, which apps you use and your files are never sent — for
an app that reads every keystroke, that is not a detail, it is the whole basis on which you would
install it at all.

If a company machine is not domain-joined or MDM-enrolled, Lapsus will never notice and will never
ask. The obligation is still there; the app simply is not the thing enforcing it.

## Why there is no copy protection

There will not be any. The source is public and .NET decompiles in a minute, so a lock would only
inconvenience the people who were going to pay and would not slow down anyone else for an afternoon.

What a licence pays for is real: the Apple Developer membership and the Windows code-signing
certificate that keep installers from being flagged, and the time that goes into the layouts,
dictionaries and bug fixes. If your company uses Lapsus, buy the seats.

## Buying

| | |
|---|---|
| **Pro** | One private person who wants to pay anyway, or a one-person company. One-time purchase. |
| **Business** | Per seat, yearly. Signed installers, MSI for Intune / Group Policy, priority support, and a written licence for your legal department. |

See [getlapsus.com/pricing](https://getlapsus.com/en/pricing/) for current prices and where to buy.
