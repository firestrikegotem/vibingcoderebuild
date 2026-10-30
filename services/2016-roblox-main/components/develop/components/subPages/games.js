import { useEffect, useRef, useState } from "react";
import {getGroupGames, getUserGames} from "../../../../services/games";
import AuthenticationStore from "../../../../stores/authentication";
import useButtonStyles from "../../../../styles/buttonStyles";
import ActionButton from "../../../actionButton";
import AssetList from "../assetList";
import request from "../../../../lib/request";

const GamesSubPage = props => {
  const buttonStyles = useButtonStyles();
  const [games, setGames] = useState(null);
  const [uploadName, setUploadName] = useState('');
  const [uploadFile, setUploadFile] = useState(null);
  const [uploadStatus, setUploadStatus] = useState(null);
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef(null);
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

  const uploadPlace = async () => {
    if (!auth.userId) {
      setUploadStatus({error: 'You must be logged in to upload a place.'});
      return;
    }
    if (!uploadFile) {
      setUploadStatus({error: 'Choose a .rbxl file first.'});
      return;
    }

    setUploading(true);
    setUploadStatus(null);
    try {
      const form = new FormData();
      form.append('name', uploadName || uploadFile.name.replace(/\.rbxl$/i, ''));
      form.append('file', uploadFile);

      // request() attaches the CSRF token and retries with the server's token when needed,
      // which every non-GET request requires.
      const response = await request('POST', '/apisite/develop/v1/places/upload', form);
      const data = response.data;

      setUploadStatus({success: 'Uploaded! Place ' + data.placeId + ' is now a playable game.'});
      setUploadFile(null);
      setUploadName('');
      if (fileRef.current) fileRef.current.value = '';
      setTimeout(() => window.location.reload(), 900);
    } catch (err) {
      console.error(err);
      setUploadStatus({error: 'Could not upload that place: ' + err.message});
    } finally {
      setUploading(false);
    }
  };

  const createBlankGame = async () => {
    const name = window.prompt('Enter a name for your new game:', 'My New Game');
    if (!name) return;

    try {
      await request('POST', '/apisite/games/v1/games/create', {
        name: name,
        templatePlaceId: 1818,
      });

      alert('Game created successfully!');
      window.location.reload();
    } catch (err) {
      console.error(err);
      alert('Error creating game: ' + err.message);
    }
  };

  return <div className='row'>
    <div className='col-12'>
      <div className='p-3 mb-3' style={{border: '1px solid #ccc', borderRadius: 4}}>
        <h3 className='mb-1'>Upload a place</h3>
        <p className='text-muted mb-2'>
          Save your game from Roblox Studio as a .rbxl file, then upload it here. The place you upload
          becomes the exact game people can join.
        </p>
        <input
          className='form-control'
          placeholder='Game name'
          value={uploadName}
          disabled={uploading}
          onChange={e => setUploadName(e.target.value)}
        />
        <input
          className='form-control mt-2'
          type='file'
          accept='.rbxl'
          ref={fileRef}
          disabled={uploading}
          onChange={e => setUploadFile(e.target.files && e.target.files[0] ? e.target.files[0] : null)}
        />
        {uploadStatus && uploadStatus.error ? <p className='text-danger mt-2 mb-0'>{uploadStatus.error}</p> : null}
        {uploadStatus && uploadStatus.success ? <p className='text-success mt-2 mb-0'>{uploadStatus.success}</p> : null}
        <button
          className={buttonStyles.buyButton + ' mt-2'}
          disabled={uploading}
          onClick={uploadPlace}
        >
          {uploading ? 'Uploading...' : 'Upload place'}
        </button>
      </div>

      <ActionButton onClick={createBlankGame} className={buttonStyles.buyButton + ' w-auto ms-0'} label='Create New Game'/>
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
