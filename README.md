## Introduction #

TcmDCPImport is a console application allowing for a multi-threaded import of file-based dynamic component presentations into a database.

### Issue Experienced #

In an existing Tridion implementations dynamic component presentations are on disk due to legacy reasons.

TcmDCPImport was developed to import the dynamic component presentations into the database without having to republish the entire content from Tridion (300.000+ items).

### Resolution #

The following query will give a list of all component presentations present in the content data store, but where the content is not stored in the database:

    -- Select all component presentations where content is not present in database
    SELECT CPMD.PUBLICATION_ID, CPMD.COMPONENT_REF_ID, CPMD.COMPONENT_TEMPLATE_ID, CPMD.COMPONENT_OUTPUT_FORMAT
    FROM COMPONENT_PRES_META_DATA AS CPMD
    LEFT JOIN COMPONENT_PRESENTATIONS AS CP
    ON CPMD.PUBLICATION_ID = CP.PUBLICATION_ID
    AND CPMD.COMPONENT_TEMPLATE_ID = CP.TEMPLATE_ID
    AND CPMD.COMPONENT_REF_ID = CP.COMPONENT_ID
    WHERE CP.COMPONENT_ID IS NULL;

This list is then used to read each file from the Tridion content data store file system and insert it into the database.

Additional cleanup can be done using the following two queries:

Find all component presentation content which is not present in the metadata table:

    -- COMPONENT_PRESENTATIONS not present in COMPONENT_PRES_META_DATA															 
    SELECT *
    FROM COMPONENT_PRESENTATIONS AS CP
    WHERE NOT EXISTS (SELECT COMPONENT_ID
                      FROM COMPONENT_PRES_META_DATA AS CPMD
                      WHERE CPMD.PUBLICATION_ID = CP.PUBLICATION_ID
                        AND CPMD.COMPONENT_TEMPLATE_ID = CP.TEMPLATE_ID
                        AND CPMD.COMPONENT_REF_ID = CP.COMPONENT_ID)

Find all component presentation metadata for which no content is present:

    -- COMPONENT_PRES_META_DATA not present in COMPONENT_PRESENTATIONS															 
    SELECT *
    FROM COMPONENT_PRES_META_DATA AS CPMD
    WHERE NOT EXISTS (SELECT COMPONENT_ID
                      FROM COMPONENT_PRESENTATIONS AS CP
                      WHERE CPMD.PUBLICATION_ID = CP.PUBLICATION_ID
                        AND CPMD.COMPONENT_TEMPLATE_ID = CP.TEMPLATE_ID
                        AND CPMD.COMPONENT_REF_ID = CP.COMPONENT_ID)

The application itself uses .NET parallelism in order to increase the throughput while importing into the database.
