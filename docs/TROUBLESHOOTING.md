# Troubleshooting

| Problem | What to try |
| --- | --- |
| Steam connection failed | Open Steam, sign in, and launch the app again. |
| Missing games | Let the Steam Library finish loading, refresh the list, or add the App ID / Store link manually. Local metadata can be incomplete. |
| Unknown / unavailable | Choose Check games again. A missing schema or failed request is not proof of protection. |
| Protected game | Bulk actions skip protected entries. The app does not override server restrictions. |
| Unconfirmed save | Check the Last result column. Steam may retry staged changes; do not assume nothing changed. Read the state again before another action. |
| Open editor conflict | Close editors for those games before running a bulk action. |
| Missing DLL or startup error | Extract the entire release ZIP together. Install .NET Framework 4.8 if needed. |

Cancel queued stops games that have not started. Active saves finish. For bug reports, include the app version, App IDs and Last result text.
