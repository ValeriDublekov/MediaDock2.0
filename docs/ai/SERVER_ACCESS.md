# Server Access

The server-specific SSH profile is stored in the ignored repository-root file
`.server-access.sshconfig`. Read it when a task requires the production host;
do not ask the user to expose another local folder. It contains connection
metadata only, not a private key or password. The private key remains under
the user's Windows profile at the path referenced by that file.

Connect from the repository root in the existing PowerShell terminal:

```powershell
ssh -F .server-access.sshconfig mediadock
```

Keep that SSH shell open and reuse it for the task's remote commands. Avoid
starting a separate `ssh host "sudo ..."` process for each command: sudo's
authentication timestamp is associated with the terminal/session and a new
connection may prompt again.

For administrative work, run `sudo -v` once after connecting. The user must
enter the password directly at the terminal prompt; never request it in chat,
put it in a command, or pass it through an assistant input tool. Continue using
the same shell until the work is complete. If the host's sudo timestamp expires,
run `sudo -v` again in that shell. Do not change the host's sudo timeout just to
avoid reauthentication.

Some systemd one-shot operations, including the deploy service, take several
minutes. `sudo systemctl start mediadock-next-deploy.service` waits for the
whole operation and may appear idle while tests/builds run. Do not start a
second deployment or interrupt the first one; inspect its state with
`systemctl show` and its journal after it finishes. The deployment gate, backup,
migration, and readiness checks must remain enabled.

## Local Profile Setup

On another workstation, create `.server-access.sshconfig` in the repository
root using this template and replace the placeholders with that host's
approved values:

```sshconfig
Host mediadock
  HostName <server-LAN-IPv4>
  User <ssh-user>
  Port 22
  IdentityFile ~/.ssh/<private-key-filename>
  IdentitiesOnly yes
```

The profile is ignored by Git. Never add a private-key file, password, sudo
credential, or server secret to this repository. Keep the host's actual address
and access details out of tracked documentation.