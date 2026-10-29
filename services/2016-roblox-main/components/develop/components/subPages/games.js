import { useEffect, useState } from "react";
import { createUseStyles } from "react-jss";
import {getGroupGames, getUserGames} from "../../../../services/games";
import AuthenticationStore from "../../../../stores/authentication";
import useButtonStyles from "../../../../styles/buttonStyles";
import ActionButton from "../../../actionButton";
import AssetList from "../assetList";

const useStyles = createUseStyles({

});

const GamesSubPage = props => {
  const s = useStyles();
  const buttonStyles = useButtonStyles();
  const [games, setGames] = useState(null);
  const auth = AuthenticationStore.useContainer();

  useEffect(() => {
    setGames(null);

    if (props.groupId) {
      getGroupGames({
        groupId: props.groupId,
        cursor: '',
      }).then(data => setGames(data));
    }else if (auth.userId) {
      getUserGames({
        userId: auth.userId,
        cursor: '',
      }).then(data => setGames(data));
    }
  }, [auth.userId, props.groupId]);

  // --- THIS IS THE NEW FUNCTION WE ADDED ---
  const handleCreateGame = async () => {
    const name = window.prompt("Enter a name for your new game:", "My New Game");
    if (!name) return;

    try {
      // 1. Grab the CSRF token from the cookies
      const getCookie = (name) => {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) return parts.pop().split(';').shift();
        return null;
      };
      
      // Try different common cookie names used by ASP.NET
      const token = getCookie('XSRF-TOKEN') || getCookie('.AspNetCore.Antiforgery') || getCookie('csrf_token') || '';

      // 2. Tell the backend to create the game, including the token in the headers
      const response = await fetch('/apisite/games/v1/games/create', {
        method: 'POST',
        headers: { 
          'Content-Type': 'application/json',
          'x-csrf-token': token // Send the token here
        },
        credentials: 'include',
        body: JSON.stringify({
          name: name,
          templatePlaceId: 1818
        })
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || 'Server returned ' + response.status);
      }

      alert('Game created successfully!');
      window.location.reload(); 
    } catch (err) {
      console.error(err);
      alert('Error creating game: ' + err.message + '\n\nCheck the browser console (F12) or the dotnet terminal for details.');
    }
  };

  return <div className='row'>
    <div className='col-12'>
      {/* We added onClick={handleCreateGame} to the button below! */}
      <ActionButton onClick={handleCreateGame} className={buttonStyles.buyButton + ' w-auto ms-0'} label='Create New Game'/>
      <h2 className='mt-2'>Games</h2>
      {
        games ? (games.data.length === 0 ? <p className='mt-4'>You haven't created any games.</p> : <AssetList assets={games.data.map(v => {
          return {
            assetId: v.rootPlace.id,
            assetType: 9,
            name: v.name,
            universeId: v.id,

            // todo:
            isPublic: true,

          }
        })}/>) : null
      }
    </div>
  </div>
}

export default GamesSubPage;