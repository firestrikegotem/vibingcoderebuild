import React from "react";
import { createUseStyles } from "react-jss";
import Link from "../components/link";
import AuthenticationStore from "../stores/authentication";

const useStyles = createUseStyles({
  page: {
    minHeight: 'calc(100vh - 240px)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: '48px 16px',
    backgroundImage: 'url(/img/bloxyn_signup_bg_v2.jpg)',
    backgroundSize: 'cover',
    backgroundPosition: 'center',
  },
  card: {
    backgroundColor: '#fff',
    borderRadius: '6px',
    boxShadow: '0 2px 8px rgba(0, 0, 0, 0.3)',
    maxWidth: '520px',
    width: '100%',
    padding: '32px',
    textAlign: 'center',
  },
  logo: {
    maxWidth: '220px',
    width: '100%',
    marginBottom: '16px',
  },
  heading: {
    fontSize: '26px',
    fontWeight: 700,
    color: '#343434',
    marginBottom: '8px',
  },
  text: {
    fontSize: '15px',
    color: '#666',
    marginBottom: '24px',
  },
  actions: {
    display: 'flex',
    gap: '12px',
    justifyContent: 'center',
    flexWrap: 'wrap',
  },
  button: {
    display: 'inline-block',
    backgroundColor: '#00A23D',
    color: '#fff',
    fontSize: '16px',
    fontWeight: 600,
    textDecoration: 'none',
    borderRadius: '4px',
    padding: '10px 28px',
    '&:hover': {
      backgroundColor: '#008a34',
      color: '#fff',
      textDecoration: 'none',
    },
  },
  secondary: {
    backgroundColor: '#fff',
    color: '#00A23D',
    border: '1px solid #00A23D',
    '&:hover': {
      backgroundColor: '#f2fbf5',
      color: '#008a34',
    },
  },
})

const getReturnUrl = () => {
  if (typeof window === 'undefined') return '/home';
  const params = new URLSearchParams(window.location.search);
  return params.get('returnUrl') || '/home';
}

const IndexPage = props => {
  const s = useStyles();
  const auth = AuthenticationStore.useContainer();

  // Wait for the session check, otherwise a signed-in visitor sees the logged-out page first.
  if (auth.isPending) return null;

  if (auth.isAuthenticated) {
    if (typeof window !== 'undefined') {
      window.location.href = getReturnUrl();
    }
    return null;
  }

  return <div className={s.page}>
    <div className={s.card}>
      <img className={s.logo} src='/img/bloxyn_logo.png' alt='Bloxyn'/>
      <h1 className={s.heading}>Welcome to Bloxyn</h1>
      <p className={s.text}>
        Build, play and share. Log in to jump back into your games, or make a new account to get started.
      </p>
      <div className={s.actions}>
        <Link href='/login'>
          <a className={s.button}>Log In</a>
        </Link>
        {/* signup is a server-rendered page, so it gets a plain anchor instead of a client-side route */}
        <a href='/auth/signup' className={`${s.button} ${s.secondary}`}>Sign Up</a>
      </div>
    </div>
  </div>
}

IndexPage.getInitialProps = () => {
  return {
    title: 'Bloxyn',
  }
}

export default IndexPage;
