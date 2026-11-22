export function Login({ pin, status }: { pin: string; status: string }) {
  const code = (pin || "····").split("");
  return (
    <section className="login">
      <div className="brand brand-lg"><i className="brand-mark" />warp</div>
      <h1 className="login-title">Sign in with Plex</h1>
      <ol className="login-steps">
        <li>On your phone or computer, open <b>plex.tv/link</b></li>
        <li>Enter this code</li>
      </ol>
      <div className="pin">{code.map((char, index) => <span key={index}>{char}</span>)}</div>
      <p className="meta-line">{status}</p>
    </section>
  );
}
