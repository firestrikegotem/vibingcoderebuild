import React, { useEffect, useState } from "react";
import { createUseStyles } from "react-jss";
import getFlag from "../../lib/getFlag";
import request from "../../lib/request";
import DownloadButton from "./components/downloadButton";

const useDownloadStyles = createUseStyles({
    title: {
        color: '#343434',
        fontSize: '32px',
        fontWeight: '700',
    },
    subTitle: {
        fontSize: '14px',
    },
    empty: {
        border: '1px solid #e3e3e3',
        borderRadius: '4px',
        padding: '24px',
        color: '#666666',
        fontSize: '14px',
    },
})

const formatSize = bytes => {
    if (!bytes) return '';
    const mb = bytes / (1024 * 1024);
    if (mb >= 1) return mb.toFixed(1) + ' MB';
    return Math.max(1, Math.round(bytes / 1024)) + ' KB';
}

const Download = props => {
    const s = useDownloadStyles();
    const [clients, setClients] = useState(null);

    useEffect(() => {
        // The list is whatever player builds are sitting in the site's downloads folder, so a new
        // build shows up here without a code change.
        const configured = getFlag('downloadGameClients', []);
        if (configured && configured.length) {
            setClients(configured);
            return;
        }
        request('GET', '/apisite/download/v1/clients')
            .then(result => setClients(result.data && result.data.data ? result.data.data : []))
            .catch(() => setClients([]));
    }, []);

    return <div className='container'>
        <div className='row'>
            <div className='col-12 mb-4'>
                <h1 className={s.title}>Download</h1>
                <p className={s.subTitle}>Download the BLOXYN Player to get into the game.</p>
            </div>
        </div>
        <div className='row'>
            {
                clients === null ? null : (clients.length === 0 ?
                    <div className='col-12'>
                        <div className={s.empty}>
                            The BLOXYN Player is not available yet. Check back soon.
                        </div>
                    </div> :
                    clients.map(v => {
                        return <DownloadButton
                            key={v.fileName || v.title + v.url}
                            url={v.url}
                            title={v.title || v.name}
                            size={v.sizeInBytes ? formatSize(v.sizeInBytes) : undefined}
                            imageUrl={v.imageUrl || '/img/bloxyn_icon.png'}
                        />
                    }))
            }
        </div>
    </div>
}

export default Download;
