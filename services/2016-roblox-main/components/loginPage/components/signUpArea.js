import { createUseStyles } from "react-jss";
import ActionButton from "../../actionButton";

const useStyles = createUseStyles({
    wrapper: {
        padding: '8px 4px',
    },
    notAMemberText: {
        fontSize: '12px',
        fontWeight: 700,
        letterSpacing: '1.6px',
        textTransform: 'uppercase',
        color: '#8b9298',
        marginBottom: '8px',
    },
    header: {
        fontSize: '22px',
        fontWeight: 700,
        letterSpacing: '.3px',
        textTransform: 'uppercase',
        color: '#2e2e2e',
        marginBottom: '6px',
    },
    tagline: {
        fontSize: '13.5px',
        color: '#6a7178',
        marginBottom: '18px',
    },
    points: {
        listStyle: 'none',
        margin: '0 0 22px 0',
        padding: 0,
        fontSize: '13.5px',
        lineHeight: 2,
        color: '#4a5158',
    },
    point: {
        display: 'flex',
        alignItems: 'baseline',
        gap: '9px',
    },
    dot: {
        width: '7px',
        height: '7px',
        borderRadius: '50%',
        background: '#d81f12',
        flex: 'none',
    },
    legal: {
        fontSize: '12px',
        color: '#7e858b',
        lineHeight: 1.6,
        marginTop: '16px',
        marginBottom: 0,
    },
})

const SignUpArea = props => {
    const s = useStyles();
    return <div className={s.wrapper}>
        <p className={s.notAMemberText}>Not a member?</p>
        <h2 className={s.header}>Sign up and start having fun</h2>
        <p className={s.tagline}>Join in a few seconds.</p>
        <ul className={s.points}>
            <li className={s.point}><span className={s.dot}></span><span>Build anything you can imagine</span></li>
            <li className={s.point}><span className={s.dot}></span><span>Play with friends from around the world</span></li>
            <li className={s.point}><span className={s.dot}></span><span>Free to join, free to play</span></li>
        </ul>
        <ActionButton label='Sign Up' onClick={(e) => {
            window.location.href = '/auth/signup';
        }}></ActionButton>
        <p className={s.legal}>By signing up you agree to our Terms of Service and Privacy Policy.</p>
    </div>
}

export default SignUpArea;
