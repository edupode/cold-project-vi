Julia's Launcher 0.3.22: no more crash loop on the first start of a version, and your preset stays yours.

- The first start of a new version warms up and restarts once. When that warm-up start crashed, every later start was a warm-up again and crashed the same way (an RX 7600 nine times in a row). Now a warm-up that fails at once is not asked for again: the next start is an ordinary one.
- Automatic settings no longer change your preset at every opening: it is picked once for a new PC, again only if its graphics card or memory change, or when you press "Automatic for this PC". 0.3.21 could move a PC to Ultra Low that played fine on another preset; if that happened to you, pick your preset again once.
