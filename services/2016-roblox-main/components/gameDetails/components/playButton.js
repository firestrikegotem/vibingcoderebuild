import { useState } from "react";
import { createUseStyles } from "react-jss";
import { launchGame } from "../../../services/games";
import AuthenticationStore from "../../../stores/authentication";
import useButtonStyles from "../../../styles/buttonStyles";
import ActionButton from "../../actionButton";

const useStyles = createUseStyles({
  buttonWrapper: {
    width: '100%',
    maxWidth: '200px',
  },
  button: {
    width: '100%',
    paddingTop: '2px',
    paddingBottom: '4px',
  },
})

/**
 * Play button
 * @param {{placeId: number}} props 
 * @returns 
 */
const PlayButton = props => {
  const [error, setError] = useState(null);
  const auth = AuthenticationStore.useContainer();
  const s = useStyles();
  const buttonStyles = useButtonStyles();
  
  const onClick = e => {
    e && e.preventDefault && e.preventDefault();
    
    if (!auth.isAuthenticated) {
      window.location.href = '/Login';
      return;
    }
    
    // Force launch the game using the backend API
    launchGame({
      placeId: props.placeId,
    }).catch(err => {
      // Show the error on the screen if it fails
      setError(err.message || "Failed to launch game. The backend endpoint might be missing.");
    });
  }

  return <div className='row'>
    <div className={'col-12 mx-auto ' + s.buttonWrapper}>
      {error && <p className='text-danger mb-1 mt-1'>{error}</p>}
      <ActionButton label='Play' className={s.button + ' ' + buttonStyles.buyButton} onClick={onClick}></ActionButton>
    </div>
  </div>
}

export default PlayButton;